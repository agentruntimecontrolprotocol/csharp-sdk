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
                _logger.LogError(ex, "Dispatch error for type {Type}", env.Type);
            }
        }
    }

    private async Task DispatchAsync(Envelope env, CancellationToken cancellationToken)
    {
        switch (env.Type)
        {
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
            case MessageTypeNames.SessionBye:
                IsClosed = true;
                await CloseAsync(reason: (env.Payload as SessionByePayload)?.Reason, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobSubmit:
                if (env.Payload is JobSubmitPayload submit)
                    await HandleJobSubmitAsync(env, submit, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobCancel:
                if (env.Payload is JobCancelPayload cancel)
                {
                    if (JobId.TryParse(cancel.JobId, null, out var jid))
                        _server.JobManager.Cancel(jid, Principal?.Subject, cancel.Reason);
                }
                break;
            case MessageTypeNames.JobSubscribe:
                if (env.Payload is JobSubscribePayload sub)
                    await HandleSubscribeAsync(env, sub, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.JobUnsubscribe:
                if (env.Payload is JobUnsubscribePayload unsub)
                {
                    if (JobId.TryParse(unsub.JobId, null, out var jid))
                        _server.Subscriptions.Unsubscribe(jid, SessionId);
                }
                break;
            default:
                _logger.LogDebug("Unhandled inbound type {Type}", env.Type);
                break;
        }
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
