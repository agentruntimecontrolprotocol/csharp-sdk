// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

public sealed partial class SessionState
{
    private async Task ReceiverLoop(CancellationToken cancellationToken)
    {
        await foreach (var env in _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false))
        {
            _lastInboundAt = _options.TimeProvider.GetUtcNow();
            try
            {
                await DispatchAsync(env, cancellationToken).ConfigureAwait(false);
            }
            catch (ArcpException ex)
            {
                await SendAsync(new Envelope
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
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Spec §12: surface an unexpected failure as session.error{INTERNAL_ERROR} so the
                // peer is not left waiting forever for an acknowledgement that never arrives.
                _logger.LogError(ex, "Dispatch error for type {Type}", env.Type);
                try
                {
                    await SendAsync(new Envelope
                    {
                        Type = MessageTypeNames.SessionError,
                        SessionId = SessionId.Value,
                        Payload = new SessionErrorPayload
                        {
                            Code = ErrorCode.InternalError,
                            Message = "Internal error while processing request",
                            Retryable = true,
                        },
                    }, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Failed to send INTERNAL_ERROR for type {Type}", env.Type);
                }
            }
        }
    }

    private async Task DispatchAsync(Envelope env, CancellationToken cancellationToken)
    {
        switch (env.Type)
        {
            case MessageTypeNames.InvalidEnvelope:
                // Spec §12: surface INVALID_REQUEST to the peer so it gets feedback instead of
                // a silent drop. Detail keeps the parse-error short to avoid echoing bytes back.
                var parseError = (env.Payload as InvalidEnvelopePayload)?.ParseError ?? "malformed envelope";
                throw new InvalidRequestException("Malformed ARCP envelope", parseError);
            case MessageTypeNames.SessionHello:
                await HandleHelloAsync(env, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.SessionResume:
                if (env.Payload is SessionResumePayload resume)
                    await HandleResumeAsync(env, resume, cancellationToken).ConfigureAwait(false);
                else
                    throw new InvalidRequestException("session.resume payload missing");
                break;
            case MessageTypeNames.SessionPing:
                await HandlePingAsync(env, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.SessionPong:
                // clock-skew telemetry only; no-op.
                break;
            case MessageTypeNames.SessionAck:
                await HandleAckAsync(env, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.SessionListJobs:
                await HandleListJobsAsync(env, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.SessionClose:
            case MessageTypeNames.SessionBye:
                // Spec §6.7: client-sent session.close (or the deprecated session.bye alias) is
                // acknowledged with session.closed (emitted by CloseAsync). In-flight jobs are
                // rooted at the runtime token, so this does NOT terminate them.
                await CloseAsync(reason: (env.Payload as SessionByePayload)?.Reason, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobSubmit:
                if (env.Payload is JobSubmitPayload submit)
                    await HandleJobSubmitAsync(env, submit, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobCancel:
                if (env.Payload is JobCancelPayload cancel)
                    await HandleJobCancelAsync(cancel, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobSubscribe:
                if (env.Payload is JobSubscribePayload sub)
                    await HandleSubscribeAsync(env, sub, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobUnsubscribe:
                if (env.Payload is JobUnsubscribePayload unsub)
                {
                    if (JobId.TryParse(unsub.JobId, null, out var jid))
                    {
                        _server.Subscriptions.Unsubscribe(jid, SessionId);
                        _subscribeMarks.TryRemove(jid, out _);
                    }
                }
                break;
            default:
                _logger.LogDebug("Unhandled inbound type {Type}", env.Type);
                break;
        }
    }

    private async Task HandleJobCancelAsync(JobCancelPayload cancel, CancellationToken cancellationToken)
    {
        if (!JobId.TryParse(cancel.JobId, null, out var jid))
            throw new InvalidRequestException("Invalid job_id");

        // Cancellation authority is scoped to the submitting session (spec §7.6, §14). A foreign
        // session throws PERMISSION_DENIED; an unknown job returns false → JOB_NOT_FOUND (spec §12).
        var cancelled = _server.JobManager.Cancel(jid, SessionId, cancel.Reason);
        if (!cancelled)
            throw new JobNotFoundException($"job {cancel.JobId} not found");

        // Spec §7.4: acknowledge with job.cancelled before the run-loop emits the terminal
        // job.error{CANCELLED, final_status:"cancelled"}.
        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobCancelled,
            SessionId = SessionId.Value,
            JobId = jid.Value,
            Payload = new JobCancelledPayload { JobId = jid.Value, Reason = cancel.Reason },
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandlePingAsync(Envelope env, CancellationToken cancellationToken)
    {
        if (env.Payload is not SessionPingPayload p) return;
        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionPong,
            SessionId = SessionId.Value,
            Payload = new SessionPongPayload
            {
                PingNonce = p.Nonce,
                ReceivedAt = _options.TimeProvider.GetUtcNow(),
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleAckAsync(Envelope env, CancellationToken cancellationToken)
    {
        if (env.Payload is not SessionAckPayload ack) return;
        Interlocked.Exchange(ref _lastAckedSeq, ack.LastProcessedSeq);
        EventLog.Trim(ack.LastProcessedSeq);
        var lag = EventLog.HighWatermark - ack.LastProcessedSeq;
        if (lag <= _options.BackPressureThreshold) return;

        await EmitEventAsync(new Envelope
        {
            Type = MessageTypeNames.JobEvent,
            SessionId = SessionId.Value,
            Payload = new JobEventPayload
            {
                Kind = EventKinds.Status,
                Ts = _options.TimeProvider.GetUtcNow(),
                Body = ArcpJson.ToJsonElement(new StatusBody
                {
                    Phase = "back_pressure",
                    Message = $"consumer lag {lag} events",
                }),
            },
        }, cancellationToken).ConfigureAwait(false);
    }
}
