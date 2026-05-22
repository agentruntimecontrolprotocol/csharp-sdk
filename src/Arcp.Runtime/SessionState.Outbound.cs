// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Arcp.Runtime.Credentials;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

public sealed partial class SessionState
{
    private async Task SenderLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var env in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _transport.SendAsync(env, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Outbound transport closed");
        }
    }

    private async Task HeartbeatLoop(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatIntervalSec)), _options.TimeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_heartbeatNegotiated) continue;
                if (IsClosed) return;

                var idle = _options.TimeProvider.GetUtcNow() - _lastInboundAt;
                if (idle.TotalSeconds >= _options.HeartbeatIntervalSec * 2)
                {
                    _logger.LogWarning("Heartbeat lost on session {SessionId}", SessionId);
                    await CloseAsync(reason: "HEARTBEAT_LOST", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await SendAsync(new Envelope
                {
                    Type = MessageTypeNames.SessionPing,
                    SessionId = SessionId.Value,
                    Payload = new SessionPingPayload
                    {
                        Nonce = "p_" + Ulid.NewUlid(),
                        SentAt = _options.TimeProvider.GetUtcNow(),
                    },
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async ValueTask EmitJobEnvelopeAsync(Envelope env, CancellationToken cancellationToken)
    {
        // Append the event to this owning session's log if it's an event/result/error.
        var stamped = env.Type is MessageTypeNames.JobEvent or MessageTypeNames.JobResult or MessageTypeNames.JobError
            ? EventLog.Append(env)
            : env;

        await SendAsync(stamped, cancellationToken).ConfigureAwait(false);
        FanOutToSubscribers(env, stamped, cancellationToken);
    }

    private void FanOutToSubscribers(Envelope env, Envelope stamped, CancellationToken cancellationToken)
    {
        // Spec §7.6.
        if (env.JobId is not { } jobIdStr) return;
        if (!JobId.TryParse(jobIdStr, null, out var jid)) return;

        foreach (var sub in _server.Subscriptions.SubscribersOf(jid))
        {
            if (sub.Value == SessionId.Value) continue;
            _server.GetSession(sub)?.EmitToSubscriber(stamped, cancellationToken);
        }
    }

    internal void EmitToSubscriber(Envelope env, CancellationToken cancellationToken)
    {
        // Re-stamp for the subscriber's session_id and event_seq.
        var rekeyed = RedactCredentialSecretsForSubscriber(env) with { SessionId = SessionId.Value };
        var stamped = EventLog.Append(rekeyed);
        _outbound.Writer.TryWrite(stamped);
    }

    private Envelope RedactCredentialSecretsForSubscriber(Envelope env)
    {
        if (env.Payload is not JobEventPayload payload || env.JobId is not { } jobId)
            return env;
        if (!JobId.TryParse(jobId, null, out var parsed) ||
            !_server.JobManager.TryGet(parsed, out var job) ||
            string.Equals(Principal?.Subject, job?.SubmitterPrincipal, StringComparison.Ordinal))
        {
            return env;
        }

        return env with { Payload = CredentialRedaction.RedactCredentialRotation(payload) };
    }

    private async ValueTask EmitEventAsync(Envelope env, CancellationToken cancellationToken)
    {
        var stamped = EventLog.Append(env);
        await SendAsync(stamped, cancellationToken).ConfigureAwait(false);
    }
}
