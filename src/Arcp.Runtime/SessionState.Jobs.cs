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

        // Spec §6.4/§6.7: a job's lifetime is rooted at the runtime, not this session. Session
        // teardown (heartbeat loss, graceful close, transport drop) stops streaming to this
        // transport but MUST NOT cancel the job — it keeps running and stays resumable.
        var jobLifetime = _server.JobManager.RuntimeToken;

        try
        {
            var submission = await _server.JobManager
                .SubmitAsync(submit, SessionId, Principal?.Subject, emit, inboundTraceId, jobLifetime, cancellationToken)
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

            // Spec §7.2: a replay re-acknowledges the existing job but MUST NOT invoke the agent
            // again — re-running would re-emit events, re-emit a terminal result, and reset a
            // terminal job back to Running. Only fresh submissions are dispatched to the agent.
            if (submission.IsReplay)
                return;

            // Resolve agent and run. The run task is rooted at the runtime token (not _cts.Token)
            // so it outlives this session (spec §6.7).
            var resolved = _server.AgentRegistry.Resolve(job.Agent).Agent;
            _ = Task.Run(() => _server.JobManager.RunAsync(job, resolved, emit, jobLifetime), jobLifetime);
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

        var subscriberSeesOwnerSecrets = string.Equals(Principal?.Subject, job.SubmitterPrincipal, StringComparison.Ordinal);

        // Spec §7.6: history-snapshot and live-subscription registration MUST be atomic, otherwise an
        // event emitted in the window between them is lost (or duplicated). Hold the subscriber's emit
        // gate across the whole replay so no live fan-out for this job can interleave ahead of the
        // replayed history, and register + snapshot atomically under the job's buffer lock so the
        // boundary (highWaterIndex) is exact. Any fanned-out event with JobEventIndex ≤ the boundary
        // was already replayed and is skipped by EmitToSubscriberAsync; everything after is delivered
        // live exactly once.
        await _emitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = job.RegisterSubscriberAndSnapshot(
                () => _server.Subscriptions.Subscribe(job.JobId, SessionId),
                out var highWater);

            // Everything with index ≤ highWater is delivered here (or suppressed if no history was
            // requested); live fan-out delivers only index > highWater. Set the boundary now: any
            // concurrent fan-out for this job blocks on _emitGate until we release, then honors it.
            _subscribeMarks[jid] = highWater;
            if (!sub.History) history = Array.Empty<Envelope>();

            await WriteToOutboundAsync(new Envelope
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

            // Replay matching prior events, in original order, before live events arrive.
            var fromSeq = sub.FromEventSeq;
            foreach (var historic in history)
            {
                if (fromSeq is { } f && historic.JobEventIndex is { } idx && idx <= f) continue;
                var rekeyed = (subscriberSeesOwnerSecrets ? historic : RedactForNonOwner(historic, job))
                    with
                { SessionId = SessionId.Value };
                var stamped = EventLog.Append(rekeyed);
                await WriteToOutboundAsync(stamped, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _emitGate.Release();
        }
    }

    private static Envelope RedactForNonOwner(Envelope env, Job job)
    {
        if (env.Payload is not JobEventPayload payload) return env;
        return env with { Payload = Credentials.CredentialRedaction.RedactCredentialRotation(payload) };
    }
}
