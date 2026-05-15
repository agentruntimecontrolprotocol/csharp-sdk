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

    public SessionId SessionId { get; private set; }

    public IReadOnlyList<string> EffectiveFeatures { get; private set; } = Array.Empty<string>();

    public string? ResumeToken { get; private set; }

    public IReadOnlyList<AgentInventoryEntry> Agents { get; private set; } = Array.Empty<AgentInventoryEntry>();

    public RuntimeInfo? Runtime { get; private set; }

    public int? HeartbeatIntervalSec { get; private set; }

    public long LastReceivedSeq => Interlocked.Read(ref _lastReceivedSeq);

    public ArcpClient(ITransport transport, ArcpClientOptions options)
    {
        _transport = transport;
        _options = options;
    }

    public static async Task<ArcpClient> ConnectAsync(ITransport transport, ArcpClientOptions options, CancellationToken cancellationToken = default)
    {
        var client = new ArcpClient(transport, options);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

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

    public async ValueTask DisposeAsync()
    {
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
        _cts.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
