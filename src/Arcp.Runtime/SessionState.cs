// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arcp.Core.Auth;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Store;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

/// <summary>One live ARCP session on the server side. Owns the transport, the inbound dispatcher,
/// the outbound channel, the event log, and the heartbeat + ack timers (spec §6).</summary>
public sealed partial class SessionState : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly ArcpServer _server;
    private readonly ArcpServerOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<Envelope> _outbound;
    private readonly CancellationTokenSource _cts;

    /// <summary>Serializes event_seq assignment with the outbound enqueue so wire order always matches
    /// assigned event_seq under concurrent emitters (spec §8.3).</summary>
    private readonly SemaphoreSlim _emitGate = new(1, 1);

    /// <summary>Per-subscribed-job replay boundary (job-local event index). A fanned-out event whose
    /// <see cref="Arcp.Core.Wire.Envelope.JobEventIndex"/> is ≤ this mark was already delivered by the
    /// subscribe history replay and is dropped to avoid a duplicate at the boundary (spec §7.6).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Arcp.Core.Ids.JobId, long> _subscribeMarks = new();

    private long _lastAckedSeq;
    private bool _heartbeatNegotiated;
    private bool _ackNegotiated;
    private DateTimeOffset _lastInboundAt;

    /// <summary>Gets the session id.</summary>
    public SessionId SessionId { get; private set; } = SessionId.New();

    /// <summary>Gets the resume token.</summary>
    public string ResumeToken { get; private set; } = MintResumeToken();

    /// <summary>Gets the principal.</summary>
    public AuthPrincipal? Principal { get; private set; }

    /// <summary>Gets the effective features.</summary>
    public IReadOnlyList<string> EffectiveFeatures { get; private set; } = Array.Empty<string>();

    /// <summary>Gets the event log. Sized from <see cref="ArcpServerOptions.EventLogCapacity"/>.</summary>
    public EventLog EventLog { get; private set; }

    /// <summary>Adopt the durable resumable state from a prior session of the same id. Called
    /// when a client supplies a still-valid resume token (spec §6.3).</summary>
    internal void AdoptResumableStateFrom(SessionState prior)
    {
        SessionId = prior.SessionId;
        EventLog = prior.EventLog;
    }

    /// <summary>Gets the is closed.</summary>
    public bool IsClosed { get; private set; }

    internal SessionState(ITransport transport, ArcpServer server, ArcpServerOptions options, ILogger logger, CancellationToken cancellation)
    {
        _transport = transport;
        _server = server;
        _options = options;
        _logger = logger;
        _outbound = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(options.BackPressureThreshold * 2)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        EventLog = new EventLog(options.EventLogCapacity);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _lastInboundAt = options.TimeProvider.GetUtcNow();
    }

    /// <summary>Run (asynchronous).</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var token = linked.Token;
        var sender = Task.Run(() => SenderLoop(token), token);
        var heartbeat = Task.Run(() => HeartbeatLoop(token), token);
        try
        {
            await ReceiverLoop(token).ConfigureAwait(false);
        }
        finally
        {
            _cts.Cancel();
            _outbound.Writer.TryComplete();
            try { await sender.ConfigureAwait(false); } catch (OperationCanceledException) { }
            try { await heartbeat.ConfigureAwait(false); } catch (OperationCanceledException) { }
            IsClosed = true;
            _server.RemoveSession(this);
        }
    }

    /// <summary>Send (asynchronous).</summary>
    public ValueTask SendAsync(Envelope env, CancellationToken cancellationToken = default) =>
        _outbound.Writer.WriteAsync(env, cancellationToken);

    /// <summary>Close (asynchronous).</summary>
    public async ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (IsClosed) return;
        IsClosed = true;
        try
        {
            // Spec §6.7: the runtime's graceful-close wire type is session.closed. Write it straight
            // to the transport (not the outbound channel) so it is flushed before teardown — enqueuing
            // it and then cancelling the sender loop would race the ack away.
            await _transport.SendAsync(new Envelope
            {
                Type = MessageTypeNames.SessionClosed,
                SessionId = SessionId.Value,
                Payload = new SessionByePayload { Reason = reason },
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Transport may already be closed; suppress on close path.
        }
        _outbound.Writer.TryComplete();
        _cts.Cancel();
        await _transport.CloseAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    private static string MintResumeToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return "rt_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Dispose (asynchronous).</summary>
    public async ValueTask DisposeAsync()
    {
        if (IsClosed) return;
        await CloseAsync().ConfigureAwait(false);
        _cts.Dispose();
        _emitGate.Dispose();
    }
}
