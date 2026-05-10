using System.Text.Json;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Extensions;
using ARCP.Ids;
using ARCP.Messages.Session;
using ARCP.Runtime;
using ARCP.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ARCP.Client;

/// <summary>
/// Client-side companion to <see cref="ARCPRuntime" />. Connects via a
/// transport, completes the handshake, and exposes a small set of typed
/// outbound APIs (more arrive in Phase 3+).
/// </summary>
public sealed class ARCPClient : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<ARCPClient> _logger;
    private readonly Task _receiveLoop;
    private readonly CancellationTokenSource _cts = new();
    private TaskCompletionSource<SessionAccepted>? _acceptedTcs;
    private TaskCompletionSource<SessionRejected>? _rejectedTcs;

    private ARCPClient(
        ITransport transport,
        JsonSerializerOptions jsonOptions,
        ILogger<ARCPClient>? logger)
    {
        _transport = transport;
        _jsonOptions = jsonOptions;
        _logger = logger ?? NullLogger<ARCPClient>.Instance;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>The session id assigned by the runtime once the handshake completes.</summary>
    public SessionId? SessionId { get; private set; }

    /// <summary>The capabilities negotiated with the runtime.</summary>
    public Capabilities? NegotiatedCapabilities { get; private set; }

    /// <summary>The runtime identity broadcast in <c>session.accepted</c>.</summary>
    public RuntimeIdentity? RuntimeIdentity { get; private set; }

    /// <summary>
    /// Connect over <paramref name="transport" />, send <c>session.open</c>,
    /// and await <c>session.accepted</c> or <c>session.rejected</c>.
    /// </summary>
    /// <param name="transport">The configured transport.</param>
    /// <param name="auth">Credentials.</param>
    /// <param name="client">Client identity block.</param>
    /// <param name="capabilities">Requested capabilities.</param>
    /// <param name="messageRegistry">Optional message registry override.</param>
    /// <param name="extensionRegistry">Optional extension registry.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open, authenticated client.</returns>
    /// <exception cref="ARCPException">If the runtime rejects the session.</exception>
    public static async Task<ARCPClient> ConnectAsync(
        ITransport transport,
        AuthCredential auth,
        ClientIdentity client,
        Capabilities capabilities,
        MessageTypeRegistry? messageRegistry = null,
        ExtensionRegistry? extensionRegistry = null,
        ILogger<ARCPClient>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(capabilities);

        JsonSerializerOptions jsonOptions = EnvelopeJson.CreateOptions(
            messageRegistry ?? MessageTypeRegistry.CoreCatalog(),
            extensionRegistry);

        ARCPClient self = new(transport, jsonOptions, logger);
        try
        {
            self._acceptedTcs = new TaskCompletionSource<SessionAccepted>(TaskCreationOptions.RunContinuationsAsynchronously);
            self._rejectedTcs = new TaskCompletionSource<SessionRejected>(TaskCreationOptions.RunContinuationsAsynchronously);

            MessageId openId = MessageId.New();
            await self.SendAsync(new Envelope.Envelope
            {
                Arcp = ProtocolVersion.Wire,
                Id = openId,
                Type = "session.open",
                Timestamp = DateTimeOffset.UtcNow,
                Payload = new SessionOpen(auth, client, capabilities),
            }, cancellationToken).ConfigureAwait(false);

            using CancellationTokenRegistration reg = cancellationToken.Register(() =>
            {
                self._acceptedTcs?.TrySetCanceled(cancellationToken);
                self._rejectedTcs?.TrySetCanceled(cancellationToken);
            });

            Task<SessionAccepted> acceptedTask = self._acceptedTcs.Task;
            Task<SessionRejected> rejectedTask = self._rejectedTcs.Task;
            Task winner = await Task.WhenAny(acceptedTask, rejectedTask).ConfigureAwait(false);
            if (winner == rejectedTask)
            {
                SessionRejected rejection = await rejectedTask.ConfigureAwait(false);
                throw new ARCPException(rejection.Code, rejection.Message);
            }

            SessionAccepted accepted = await acceptedTask.ConfigureAwait(false);
            self.SessionId = accepted.SessionId;
            self.NegotiatedCapabilities = accepted.Capabilities;
            self.RuntimeIdentity = accepted.Runtime;
            return self;
        }
        catch
        {
            await self.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Send a <c>ping</c> and await the corresponding <c>pong</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The runtime's reported receive timestamp.</returns>
    public async Task<DateTimeOffset> PingAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        TaskCompletionSource<DateTimeOffset> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MessageId pingId = MessageId.New();
        _pendingPongs[pingId] = tcs;

        await SendAsync(new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = pingId,
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Messages.Control.Ping(),
            SessionId = SessionId!.Value,
        }, cancellationToken).ConfigureAwait(false);

        using CancellationTokenRegistration reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Close the session gracefully.</summary>
    /// <param name="reason">Optional reason recorded in <c>session.close</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (SessionId is { } sid)
        {
            await SendAsync(new Envelope.Envelope
            {
                Arcp = ProtocolVersion.Wire,
                Id = MessageId.New(),
                Type = "session.close",
                Timestamp = DateTimeOffset.UtcNow,
                Payload = new SessionClose(Reason: reason),
                SessionId = sid,
            }, cancellationToken).ConfigureAwait(false);
        }
        await _transport.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<MessageId, TaskCompletionSource<DateTimeOffset>> _pendingPongs = new();

    private async Task ReceiveLoopAsync()
    {
        try
        {
            await foreach (Envelope.Envelope env in EnvelopeReader.ReceiveAsync(_transport, _jsonOptions, _cts.Token).ConfigureAwait(false))
            {
                switch (env.Payload)
                {
                    case SessionAccepted accepted:
                        _acceptedTcs?.TrySetResult(accepted);
                        break;
                    case SessionRejected rejected:
                        _rejectedTcs?.TrySetResult(rejected);
                        break;
                    case Messages.Control.Pong pong:
                        if (_pendingPongs.TryRemove(pong.AckFor, out TaskCompletionSource<DateTimeOffset>? tcs))
                        {
                            tcs.TrySetResult(pong.ReceivedAt);
                        }
                        break;
                    case Messages.Control.Nack nack:
                        _logger.LogWarning("Received nack {Code}: {Message}", nack.Code, nack.Message);
                        if (nack.AckFor is { } ackFor && _pendingPongs.TryRemove(ackFor, out TaskCompletionSource<DateTimeOffset>? pendingTcs))
                        {
                            pendingTcs.TrySetException(new ARCPException(nack.Code, nack.Message));
                        }
                        break;
                    default:
                        _logger.LogDebug("Client received unhandled envelope type {Type}.", env.Type);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receive loop terminated unexpectedly.");
            _acceptedTcs?.TrySetException(ex);
            _rejectedTcs?.TrySetException(ex);
            foreach (TaskCompletionSource<DateTimeOffset> tcs in _pendingPongs.Values)
            {
                tcs.TrySetException(ex);
            }
        }
    }

    private async Task SendAsync(Envelope.Envelope envelope, CancellationToken cancellationToken)
    {
        WireFrame frame = EnvelopeReader.Encode(envelope, _jsonOptions);
        await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureAuthenticated()
    {
        if (SessionId is null)
        {
            throw new FailedPreconditionException("Session is not authenticated; call ConnectAsync first.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        await _transport.CloseAsync().ConfigureAwait(false);
        try
        {
            await _receiveLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best effort
        }
        _cts.Dispose();
    }
}
