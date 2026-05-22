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

        try
        {
            var submission = await _server.JobManager
                .SubmitAsync(submit, SessionId, Principal?.Subject, emit, _cts.Token, cancellationToken)
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
                Replayed = sub.History,
                Credentials = string.Equals(Principal?.Subject, job.SubmitterPrincipal, StringComparison.Ordinal)
                    ? job.Credentials
                    : null,
            },
        }, cancellationToken).ConfigureAwait(false);

        // Spec §7.6 history replay (not implemented across server-internal job buffer in this MVP).
    }
}
