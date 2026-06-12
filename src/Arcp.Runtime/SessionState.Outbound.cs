// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Channels;
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
        var isEvent = env.Type is MessageTypeNames.JobEvent or MessageTypeNames.JobResult or MessageTypeNames.JobError;

        // Spec §8.3: event_seq assignment and enqueue MUST be atomic so the single-reader outbound
        // channel delivers events in strictly monotonic order. Concurrent emitters in one session
        // (agent, lease watchdog, back-pressure status) are serialized through this gate so a higher
        // seq can never be enqueued before a lower one.
        await _emitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stamped = isEvent ? EventLog.Append(env) : env;
            await WriteToOutboundAsync(stamped, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _emitGate.Release();
        }

        await FanOutToSubscribersAsync(env, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Write to the outbound channel, tolerating a closed channel. Spec §6.7: when the
    /// session's transport has dropped, the job keeps running and the event is retained in the
    /// EventLog for resume — so a closed channel is a no-op, not a failure that faults the job.</summary>
    private async ValueTask WriteToOutboundAsync(Envelope env, CancellationToken cancellationToken)
    {
        try
        {
            await _outbound.Writer.WriteAsync(env, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Transport gone; event retained in EventLog for replay on resume (spec §6.7).
        }
    }

    private async ValueTask FanOutToSubscribersAsync(Envelope env, CancellationToken cancellationToken)
    {
        // Spec §7.6.
        if (env.JobId is not { } jobIdStr) return;
        if (!JobId.TryParse(jobIdStr, null, out var jid)) return;

        foreach (var sub in _server.Subscriptions.SubscribersOf(jid))
        {
            if (sub.Value == SessionId.Value) continue;
            var session = _server.GetSession(sub);
            if (session is not null)
                await session.EmitToSubscriberAsync(env, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async ValueTask EmitToSubscriberAsync(Envelope env, CancellationToken cancellationToken)
    {
        // Re-stamp for the subscriber's session_id and event_seq under the subscriber's emit gate so
        // append+enqueue stay atomic (spec §8.3) and the subscriber's wire order is monotonic.
        var rekeyed = RedactCredentialSecretsForSubscriber(env) with { SessionId = SessionId.Value };
        bool overflow;
        await _emitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Spec §7.6: drop events already covered by the subscribe history replay. The check runs
            // under the gate so it observes the exact boundary set while the gate was held by replay.
            if (ShouldSkipReplayedEvent(env)) return;
            var stamped = EventLog.Append(rekeyed);
            // The subscriber channel is bounded. A silent TryWrite drop would leave a gap in the
            // subscriber's event_seq (spec §8.3 requires gap-free). If the channel is full, tear the
            // subscription down deterministically instead of dropping the event silently.
            overflow = !_outbound.Writer.TryWrite(stamped);
        }
        finally
        {
            _emitGate.Release();
        }

        if (overflow)
        {
            _logger.LogWarning(
                "Subscriber session {SessionId} outbound is full; closing to avoid a silent event_seq gap (spec §8.3)",
                SessionId);
            await CloseAsync(reason: "SUBSCRIBER_OVERFLOW", cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ShouldSkipReplayedEvent(Envelope env)
    {
        if (env.JobId is not { } jobId || !JobId.TryParse(jobId, null, out var jid)) return false;
        if (env.JobEventIndex is not { } idx) return false;
        return _subscribeMarks.TryGetValue(jid, out var mark) && idx <= mark;
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
        // Route through the same emit gate as job events so back-pressure status events keep the
        // session's event_seq strictly monotonic (spec §8.3).
        await _emitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stamped = EventLog.Append(env);
            await WriteToOutboundAsync(stamped, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _emitGate.Release();
        }
    }
}
