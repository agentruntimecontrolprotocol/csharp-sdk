// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Client;

public sealed partial class ArcpClient
{
    private async Task ReaderLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var env in _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                if (env.EventSeq is { } seq)
                {
                    // Spec §8.3: event_seq is strictly monotonic and gap-free. If the new seq skips
                    // the expected successor, surface a detectable broken-session signal instead of
                    // silently accepting the gap.
                    var prev = Interlocked.Read(ref _lastReceivedSeq);
                    if (prev > 0 && seq > prev + 1) OnEventSeqGap(prev + 1, seq);
                    if (seq > prev) Interlocked.Exchange(ref _lastReceivedSeq, seq);
                }
                await DispatchAsync(env, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown; reader loop exits silently.
        }
        catch (Exception)
        {
            FailAllInFlight();
        }
    }

    private void FailAllInFlight()
    {
        foreach (var h in _handles.Values)
        {
            h.OnError(new JobErrorPayload
            {
                Code = ErrorCode.InternalError,
                Message = "Transport closed",
                Retryable = true,
            });
        }
    }

    private async Task DispatchAsync(Envelope env, CancellationToken cancellationToken)
    {
        switch (env.Type)
        {
            case MessageTypeNames.SessionWelcome:
                if (env.Payload is SessionWelcomePayload w) ApplyWelcome(env, w);
                break;
            case MessageTypeNames.SessionPing:
                if (env.Payload is SessionPingPayload p)
                    await RespondToPingAsync(p, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.SessionError:
                if (env.Payload is SessionErrorPayload err) PropagateSessionError(err);
                break;
            case MessageTypeNames.SessionJobs:
                if (env.Payload is SessionJobsPayload jobs && jobs.RequestId is { } reqId)
                {
                    if (_listJobsRequests.TryRemove(reqId, out var tcs)) tcs.TrySetResult(jobs);
                }
                break;
            case MessageTypeNames.JobAccepted:
                if (env.Payload is JobAcceptedPayload accepted && env.JobId is { } jaJid && JobId.TryParse(jaJid, null, out var jaId))
                {
                    if (_pendingSubmits.TryDequeue(out var pending))
                    {
                        pending.OnAccepted(accepted);
                        _handles[jaId] = pending;
                    }
                }
                break;
            case MessageTypeNames.JobSubscribed:
                if (env.Payload is JobSubscribedPayload subbed && env.JobId is { } jsJid && JobId.TryParse(jsJid, null, out var jsId))
                {
                    if (_subscriptions.TryGetValue(jsId, out var s2)) s2.OnSubscribed(subbed);
                }
                break;
            case MessageTypeNames.JobEvent:
                if (env.JobId is { } jeId && JobId.TryParse(jeId, null, out var jeJid))
                {
                    if (_handles.TryGetValue(jeJid, out var h2)) h2.OnEvent(env);
                    if (_subscriptions.TryGetValue(jeJid, out var sub2)) sub2.OnEvent(env);
                }
                break;
            case MessageTypeNames.JobResult:
                if (env.Payload is JobResultPayload res && env.JobId is { } jrJid && JobId.TryParse(jrJid, null, out var jrId))
                {
                    if (_handles.TryGetValue(jrId, out var h3)) h3.OnResult(res);
                    if (_subscriptions.TryGetValue(jrId, out var sub3)) sub3.OnTerminal();
                }
                break;
            case MessageTypeNames.JobError:
                if (env.Payload is JobErrorPayload jerr && env.JobId is { } jerrJid && JobId.TryParse(jerrJid, null, out var jerrId))
                {
                    if (_handles.TryGetValue(jerrId, out var h4)) h4.OnError(jerr);
                    if (_subscriptions.TryGetValue(jerrId, out var sub4)) sub4.OnTerminal();
                }
                break;
        }
    }

    private void ApplyWelcome(Envelope env, SessionWelcomePayload w)
    {
        if (env.SessionId is { } sid && SessionId.TryParse(sid, null, out var s)) SessionId = s;
        EffectiveFeatures = FeatureSet.Intersect(_options.Features, w.Capabilities.Features);
        ResumeToken = w.ResumeToken;
        Agents = w.Capabilities.Agents ?? Array.Empty<AgentInventoryEntry>();
        Runtime = w.Runtime;
        HeartbeatIntervalSec = w.HeartbeatIntervalSec;
        _welcomeTcs?.TrySetResult(w);
    }

    private ValueTask RespondToPingAsync(SessionPingPayload p, CancellationToken cancellationToken) =>
        _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionPong,
            SessionId = SessionId.Value,
            Payload = new SessionPongPayload
            {
                PingNonce = p.Nonce,
                ReceivedAt = _options.TimeProvider.GetUtcNow(),
            },
        }, cancellationToken);

    private void PropagateSessionError(SessionErrorPayload err)
    {
        var jobError = new JobErrorPayload
        {
            Code = err.Code,
            Message = err.Message,
            Retryable = err.Retryable,
            Detail = err.Detail,
        };

        foreach (var h in _handles.Values)
        {
            h.OnError(jobError);
        }

        // A submission rejected before acceptance lives in _pendingSubmits, not _handles, and a
        // list_jobs request lives in _listJobsRequests. session.error is not correlated to a
        // specific request id, so the safe contract is to fault every outstanding request — leaving
        // them pending would hang SubmitAsync/ListJobsAsync until the caller's token fires.
        while (_pendingSubmits.TryDequeue(out var pending))
        {
            pending.OnError(jobError);
        }

        foreach (var key in _listJobsRequests.Keys)
        {
            if (_listJobsRequests.TryRemove(key, out var tcs))
                tcs.TrySetException(JobHandle.ToException(err.Code, err.Message, err.Detail));
        }
    }
}
