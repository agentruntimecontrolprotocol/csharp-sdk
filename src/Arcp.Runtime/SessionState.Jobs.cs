// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;

namespace Arcp.Runtime;

public sealed partial class SessionState
{
    private async Task HandleJobSubmitAsync(Envelope env, JobSubmitPayload submit, CancellationToken cancellationToken)
    {
        Func<Envelope, CancellationToken, ValueTask> emit = (e, ct) => EmitJobEnvelopeAsync(e, ct);

        // Spec §11: forward the inbound envelope's trace context to the JobManager so a client-side
        // span flows through to job-scoped events instead of being severed by a fresh trace id.
        TraceId? inboundTraceId = null;
        if (env.TraceId is { Length: > 0 } incoming && TraceId.TryParse(incoming, null, out var parsed))
            inboundTraceId = parsed;

        try
        {
            var submission = await _server.JobManager
                .SubmitAsync(submit, SessionId, Principal?.Subject, emit, inboundTraceId, _cts.Token, cancellationToken)
                .ConfigureAwait(false);
            var job = submission.Job;
            var accepted = submission.Accepted;

            await SendAsync(new Envelope
            {
                Type = MessageTypeNames.JobAccepted,
                SessionId = SessionId.Value,
                JobId = job.JobId.Value,
                TraceId = job.TraceId?.Value,
                Payload = accepted,
            }, cancellationToken).ConfigureAwait(false);

            // Resolve agent and run.
            var resolved = _server.AgentRegistry.Resolve(job.Agent).Agent;
            _ = Task.Run(() => _server.JobManager.RunAsync(job, resolved, emit, _cts.Token), _cts.Token);
        }
        catch (ArcpException ex)
        {
            await SendSessionErrorAsync(ex, cancellationToken).ConfigureAwait(false);
            return;
        }
    }

    private ValueTask SendSessionErrorAsync(ArcpException ex, CancellationToken cancellationToken) =>
        SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionError,
            SessionId = SessionId.Value,
            Payload = new SessionErrorPayload
            {
                Code = ex.Code,
                Message = ex.Message,
                Retryable = ex.Retryable,
                Detail = ex.Detail,
            },
        }, cancellationToken);

    private async Task HandleSubscribeAsync(Envelope env, JobSubscribePayload sub, CancellationToken cancellationToken)
    {
        if (!FeatureSet.Has(EffectiveFeatures, FeatureFlags.Subscribe))
            throw new InvalidRequestException("'subscribe' feature not negotiated (spec §6.2)");
        if (!JobId.TryParse(sub.JobId, null, out var jid))
            throw new InvalidRequestException("Invalid job_id");
        if (!_server.JobManager.TryGet(jid, out var job) || job is null)
            throw new JobNotFoundException($"job {sub.JobId} not found or not visible");

        if (!_options.AuthorizationPolicy.CanObserve(job.SubmitterPrincipal, Principal))
            throw new PermissionDeniedException("Subscriber not authorized to observe job");

        // Snapshot prior events BEFORE subscribing so we can replay them, then attach the live
        // subscription. Live events from the buffer's tail to "now" could double-fire; we filter
        // by event-seq below when re-sending.
        var history = sub.History
            ? job.SnapshotEventHistory()
            : Array.Empty<Envelope>();
        var subscriberSeesOwnerSecrets = string.Equals(Principal?.Subject, job.SubmitterPrincipal, StringComparison.Ordinal);

        _server.Subscriptions.Subscribe(job.JobId, SessionId);

        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobSubscribed,
            SessionId = SessionId.Value,
            JobId = job.JobId.Value,
            Payload = new JobSubscribedPayload
            {
                JobId = job.JobId.Value,
                CurrentStatus = job.Status.ToString().ToLowerInvariant(),
                Agent = job.Agent.ToString(),
                Lease = job.Lease,
                LeaseConstraints = job.LeaseConstraints,
                ParentJobId = job.ParentJobId,
                TraceId = job.TraceId?.Value,
                SubscribedFrom = EventLog.HighWatermark,
                Replayed = sub.History && history.Count > 0,
                Credentials = subscriberSeesOwnerSecrets ? job.Credentials : null,
            },
        }, cancellationToken).ConfigureAwait(false);

        // Spec §7.6: replay matching prior events, in original order, before live events arrive.
        var fromSeq = sub.FromEventSeq;
        foreach (var historic in history)
        {
            if (fromSeq is { } f && historic.EventSeq is { } seq && seq <= f) continue;
            var rekeyed = (subscriberSeesOwnerSecrets ? historic : RedactForNonOwner(historic, job))
                with
            { SessionId = SessionId.Value };
            var stamped = EventLog.Append(rekeyed);
            await SendAsync(stamped, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Envelope RedactForNonOwner(Envelope env, Job job)
    {
        if (env.Payload is not JobEventPayload payload) return env;
        return env with { Payload = Credentials.CredentialRedaction.RedactCredentialRotation(payload) };
    }
}
