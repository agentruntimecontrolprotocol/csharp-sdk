// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;

namespace Arcp.Client;

/// <summary>An ARCP client. Manages one session and lets the caller submit jobs, list jobs, subscribe
/// to other jobs, and observe heartbeats/back-pressure (spec §6, §7).</summary>
public sealed partial class ArcpClient : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly ArcpClientOptions _options;
    private readonly CancellationTokenSource _cts = new();

    private readonly ConcurrentDictionary<JobId, JobHandle> _handles = new();
    private readonly ConcurrentQueue<JobHandle> _pendingSubmits = new();
    private readonly ConcurrentDictionary<JobId, JobSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SessionJobsPayload>> _listJobsRequests = new(StringComparer.Ordinal);

    private TaskCompletionSource<SessionWelcomePayload>? _welcomeTcs;
    private long _lastReceivedSeq;
    private Task? _readerLoop;
    private bool _disposed;

    /// <summary>Gets the session id.</summary>
    public SessionId SessionId { get; private set; }

    /// <summary>Gets the effective features.</summary>
    public IReadOnlyList<string> EffectiveFeatures { get; private set; } = Array.Empty<string>();

    /// <summary>Gets the resume token.</summary>
    public string? ResumeToken { get; private set; }

    /// <summary>Gets the agents.</summary>
    public IReadOnlyList<AgentInventoryEntry> Agents { get; private set; } = Array.Empty<AgentInventoryEntry>();

    /// <summary>Gets the runtime.</summary>
    public RuntimeInfo? Runtime { get; private set; }

    /// <summary>Gets the heartbeat interval sec.</summary>
    public int? HeartbeatIntervalSec { get; private set; }

    /// <summary>Gets the last received seq.</summary>
    public long LastReceivedSeq => Interlocked.Read(ref _lastReceivedSeq);

    /// <summary>True once an inbound <c>event_seq</c> has skipped the expected next value, indicating
    /// the session stream has a gap and SHOULD be treated as broken (and resumed once resume is
    /// wired) per spec §8.3.</summary>
    public bool IsSessionBroken { get; private set; }

    /// <summary>Raised when an inbound <c>event_seq</c> skips the expected successor (spec §8.3). The
    /// arguments are <c>(expectedSeq, receivedSeq)</c>. Handlers run on the reader loop; keep them
    /// fast and non-throwing.</summary>
    public event Action<long, long>? EventSeqGapDetected;

    private void OnEventSeqGap(long expected, long received)
    {
        IsSessionBroken = true;
        EventSeqGapDetected?.Invoke(expected, received);
    }

    /// <summary>Initializes a new instance of the <see cref="ArcpClient"/> class.</summary>
    public ArcpClient(ITransport transport, ArcpClientOptions options)
    {
        _transport = transport;
        _options = options;
    }

    /// <summary>Connect (asynchronous).</summary>
    public static async Task<ArcpClient> ConnectAsync(ITransport transport, ArcpClientOptions options, CancellationToken cancellationToken = default)
    {
        var client = new ArcpClient(transport, options);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    /// <summary>Connect (asynchronous).</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _welcomeTcs = new TaskCompletionSource<SessionWelcomePayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readerLoop = Task.Run(() => ReaderLoop(_cts.Token), _cts.Token);

        await _transport.SendAsync(BuildHello(), cancellationToken).ConfigureAwait(false);

        var welcome = await _welcomeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (welcome is null)
            throw new InternalErrorException("No session.welcome received");
    }

    private Envelope BuildHello() => new()
    {
        Type = MessageTypeNames.SessionHello,
        Payload = new SessionHelloPayload
        {
            Client = _options.Client,
            Auth = _options.Token is null ? null : new AuthCredential { Scheme = _options.AuthScheme, Token = _options.Token },
            Capabilities = new Capabilities
            {
                Encodings = _options.Encodings ?? Array.Empty<string>(),
                Features = _options.Features,
            },
        },
    };

    /// <summary>Dispose (asynchronous).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _transport.SendAsync(new Envelope
            {
                Type = MessageTypeNames.SessionBye,
                SessionId = SessionId.Value,
                Payload = new SessionByePayload { Reason = "client_close" },
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Transport may already be closed; suppress on dispose path.
        }
        try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
