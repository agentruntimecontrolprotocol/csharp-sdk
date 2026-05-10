using System.Collections.Concurrent;
using System.Text.Json;
using ARCP.Auth;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Extensions;
using ARCP.Ids;
using ARCP.Messages.Control;
using ARCP.Messages.Session;
using ARCP.Store;
using ARCP.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ARCP.Runtime;

/// <summary>
/// Server-side runtime per RFC-0001-v2 §5 / §8. Accepts a transport, drives
/// the session handshake, persists envelopes through an
/// <see cref="EventLog" />, and dispatches authenticated traffic to
/// caller-registered handlers.
/// </summary>
/// <remarks>
/// Phase 2 implements the handshake (§8) and capability negotiation (§7).
/// Phases 3–5 fill in JobManager, StreamManager, SubscriptionManager, and
/// ArtifactStore on top of the dispatch table established here.
/// </remarks>
public sealed class ARCPRuntime : IAsyncDisposable
{
    private readonly ARCPRuntimeOptions _options;
    private readonly ILogger<ARCPRuntime> _logger;
    private readonly MessageTypeRegistry _messageRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<AuthScheme, IAuthVerifier> _verifiers;

    /// <summary>Initializes a new <see cref="ARCPRuntime" /> with the given options.</summary>
    /// <param name="options">Runtime configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public ARCPRuntime(ARCPRuntimeOptions options, ILogger<ARCPRuntime>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger ?? NullLogger<ARCPRuntime>.Instance;
        _messageRegistry = options.MessageRegistry ?? MessageTypeRegistry.CoreCatalog();
        _jsonOptions = EnvelopeJson.CreateOptions(_messageRegistry, options.ExtensionRegistry);
        _verifiers = new ConcurrentDictionary<AuthScheme, IAuthVerifier>();
        foreach (IAuthVerifier verifier in options.AuthVerifiers ?? Array.Empty<IAuthVerifier>())
        {
            _verifiers[verifier.Scheme] = verifier;
        }
    }

    /// <summary>The runtime's stable event log.</summary>
    public EventLog EventLog => _options.EventLog;

    /// <summary>The negotiated <see cref="JsonSerializerOptions" />.</summary>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>The runtime identity surface.</summary>
    public RuntimeIdentity Identity => _options.Identity;

    /// <summary>
    /// Drive a single transport's session lifecycle: handshake, dispatch,
    /// teardown. Returns when the transport closes.
    /// </summary>
    /// <param name="transport">The transport to host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session has terminated.</returns>
    public async Task ServeAsync(ITransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        SessionState state = new SessionState.Unauthenticated();
        try
        {
            await foreach (Envelope.Envelope envelope in EnvelopeReader.ReceiveAsync(transport, _jsonOptions, cancellationToken).ConfigureAwait(false))
            {
                state = await HandleAsync(transport, envelope, state, cancellationToken).ConfigureAwait(false);
                if (state is SessionState.Closed)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Server stop requested; exit cleanly.
        }
        catch (InvalidArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid envelope received; closing transport.");
            await SendNackAsync(transport, ErrorCode.InvalidArgument, ex.Message, ackFor: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<SessionState> HandleAsync(
        ITransport transport,
        Envelope.Envelope envelope,
        SessionState state,
        CancellationToken cancellationToken)
    {
        // Persist the inbound envelope before processing so retries are deduped (§6.4).
        if (state is SessionState.Authenticated auth)
        {
            await _options.EventLog.AppendAsync(auth.SessionId, envelope, cancellationToken).ConfigureAwait(false);
        }

        return state switch
        {
            SessionState.Unauthenticated => await HandleUnauthenticatedAsync(transport, envelope, cancellationToken).ConfigureAwait(false),
            SessionState.Authenticating a => await HandleAuthenticatingAsync(transport, envelope, a, cancellationToken).ConfigureAwait(false),
            SessionState.Authenticated a => await HandleAuthenticatedAsync(transport, envelope, a, cancellationToken).ConfigureAwait(false),
            SessionState.Closed c => c,
            _ => throw new InternalException($"Unhandled session state {state.GetType().Name}."),
        };
    }

    private async Task<SessionState> HandleUnauthenticatedAsync(
        ITransport transport,
        Envelope.Envelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.Payload is not SessionOpen open)
        {
            _logger.LogWarning("Pre-handshake message {Type} dropped (§8.1).", envelope.Type);
            await SendNackAsync(transport, ErrorCode.FailedPrecondition,
                $"Pre-handshake message \"{envelope.Type}\" rejected (§8.1).",
                envelope.Id, cancellationToken).ConfigureAwait(false);
            return new SessionState.Unauthenticated();
        }

        try
        {
            // §7: required-but-unsupported capabilities are rejected with UNIMPLEMENTED.
            Capabilities negotiated = CapabilityNegotiator.Negotiate(open.Capabilities, _options.Capabilities);

            // §8.2: anonymous gating.
            if (open.Auth.Scheme == AuthScheme.None && !(negotiated.Anonymous ?? false))
            {
                await SendRejectedAsync(transport, envelope.Id,
                    ErrorCode.Unauthenticated,
                    "Anonymous auth requires capabilities.anonymous: true (§8.2).",
                    cancellationToken).ConfigureAwait(false);
                return new SessionState.Closed("anonymous-not-negotiated");
            }

            // §8.2: schemes deferred to v0.2.
            if (open.Auth.Scheme == AuthScheme.Mtls || open.Auth.Scheme == AuthScheme.OAuth2)
            {
                await SendRejectedAsync(transport, envelope.Id, ErrorCode.Unimplemented,
                    $"Auth scheme \"{open.Auth.Scheme}\" is not supported in this build (PLAN.md §4.1).",
                    cancellationToken).ConfigureAwait(false);
                return new SessionState.Closed("auth-scheme-unimplemented");
            }

            AuthIdentity identity;
            if (open.Auth.Scheme == AuthScheme.None)
            {
                identity = new AuthIdentity("anonymous");
            }
            else if (_verifiers.TryGetValue(open.Auth.Scheme, out IAuthVerifier? verifier))
            {
                identity = await verifier.VerifyAsync(open.Auth, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SendRejectedAsync(transport, envelope.Id, ErrorCode.Unauthenticated,
                    $"No verifier registered for scheme \"{open.Auth.Scheme}\".",
                    cancellationToken).ConfigureAwait(false);
                return new SessionState.Closed("no-verifier");
            }

            SessionId sessionId = SessionId.New();
            await SendAcceptedAsync(transport, envelope.Id, sessionId, negotiated, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Session {SessionId} accepted for {Principal}.", sessionId, identity.Principal);
            return new SessionState.Authenticated(sessionId, identity, negotiated);
        }
        catch (UnauthenticatedException ex)
        {
            await SendRejectedAsync(transport, envelope.Id, ex.Code, ex.Message, cancellationToken).ConfigureAwait(false);
            return new SessionState.Closed("auth-failed");
        }
        catch (UnimplementedException ex)
        {
            await SendRejectedAsync(transport, envelope.Id, ex.Code, ex.Message, cancellationToken).ConfigureAwait(false);
            return new SessionState.Closed("capability-unsupported");
        }
        catch (ARCPException ex)
        {
            await SendRejectedAsync(transport, envelope.Id, ex.Code, ex.Message, cancellationToken).ConfigureAwait(false);
            return new SessionState.Closed("rejected");
        }
    }

    private static Task<SessionState> HandleAuthenticatingAsync(
        ITransport transport,
        Envelope.Envelope envelope,
        SessionState.Authenticating state,
        CancellationToken cancellationToken)
    {
        _ = transport;
        _ = envelope;
        _ = state;
        _ = cancellationToken;
        // Phase 2 only supports zero-roundtrip schemes; challenge/response (§8.1 step 3)
        // arrives in v0.2 alongside multi-step bearer / OAuth flows. Today we never
        // enter this state (HandleUnauthenticatedAsync transitions directly to
        // Authenticated or Closed), but the dispatch arm is kept so the state machine
        // remains exhaustive.
        throw new UnimplementedException("§8.1", "challenge/response handshake is deferred to v0.2");
    }

    private async Task<SessionState> HandleAuthenticatedAsync(
        ITransport transport,
        Envelope.Envelope envelope,
        SessionState.Authenticated state,
        CancellationToken cancellationToken)
    {
        switch (envelope.Payload)
        {
            case Ping:
                await SendAsync(transport,
                    new Envelope.Envelope
                    {
                        Arcp = ProtocolVersion.Wire,
                        Id = MessageId.New(),
                        Type = "pong",
                        Timestamp = DateTimeOffset.UtcNow,
                        Payload = new Pong(envelope.Id, DateTimeOffset.UtcNow),
                        SessionId = state.SessionId,
                        CorrelationId = envelope.Id,
                    },
                    cancellationToken).ConfigureAwait(false);
                return state;

            case Messages.Execution.ToolInvoke invoke:
                if (_options.JobManager is { } jm)
                {
                    try
                    {
                        await jm.SubmitAsync(state.SessionId, envelope.Id, invoke, cancellationToken).ConfigureAwait(false);
                    }
                    catch (NotFoundException ex)
                    {
                        await SendNackAsync(transport, ErrorCode.NotFound, ex.Message, envelope.Id, cancellationToken).ConfigureAwait(false);
                    }
                }
                return state;

            case Messages.Control.Cancel cancel:
                if (_options.JobManager is { } jmCancel)
                {
                    await jmCancel.CancelAsync(envelope.Id, state.SessionId, cancel, cancellationToken).ConfigureAwait(false);
                }
                return state;

            case Messages.Control.Interrupt interrupt:
                if (_options.JobManager is { } jmInterrupt)
                {
                    await jmInterrupt.InterruptAsync(state.SessionId, interrupt, cancellationToken).ConfigureAwait(false);
                }
                return state;

            case Messages.Human.HumanInputResponse:
            case Messages.Human.HumanInputCancelled:
            case Messages.Human.HumanChoiceResponse:
            case Messages.Permissions.PermissionGrant:
            case Messages.Permissions.PermissionDeny:
                _options.JobManager?.DispatchResponse(envelope);
                return state;

            case SessionClose close:
                _logger.LogInformation("Session {SessionId} closed by client: {Reason}", state.SessionId, close.Reason);
                return new SessionState.Closed(close.Reason ?? "client-closed");

            default:
                if (_options.Handler is { } handler)
                {
                    await handler(envelope, state, _options, cancellationToken).ConfigureAwait(false);
                    return state;
                }
                _logger.LogDebug("Authenticated message {Type} accepted; no handler registered.", envelope.Type);
                return state;
        }
    }

    private async Task SendAsync(ITransport transport, Envelope.Envelope envelope, CancellationToken cancellationToken)
    {
        WireFrame frame = EnvelopeReader.Encode(envelope, _jsonOptions);
        await transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private Task SendAcceptedAsync(
        ITransport transport,
        MessageId correlation,
        SessionId sessionId,
        Capabilities negotiated,
        CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "session.accepted",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new SessionAccepted(sessionId, _options.Identity, negotiated),
            CorrelationId = correlation,
            SessionId = sessionId,
        };
        return SendAsync(transport, env, cancellationToken);
    }

    private Task SendRejectedAsync(
        ITransport transport,
        MessageId correlation,
        ErrorCode code,
        string message,
        CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "session.rejected",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new SessionRejected { Code = code, Message = message },
            CorrelationId = correlation,
        };
        return SendAsync(transport, env, cancellationToken);
    }

    private Task SendNackAsync(
        ITransport transport,
        ErrorCode code,
        string message,
        MessageId? ackFor,
        CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "nack",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Nack(code, message, AckFor: ackFor),
        };
        return SendAsync(transport, env, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _options.EventLog.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Configuration for <see cref="ARCPRuntime" />.</summary>
public sealed class ARCPRuntimeOptions
{
    /// <summary>Runtime identity broadcast in <c>session.accepted</c>.</summary>
    public required RuntimeIdentity Identity { get; init; }

    /// <summary>Capabilities advertised to clients (§7).</summary>
    public required Capabilities Capabilities { get; init; }

    /// <summary>Event log used for envelope persistence.</summary>
    public required EventLog EventLog { get; init; }

    /// <summary>Auth verifiers, indexed at construction by their <see cref="IAuthVerifier.Scheme" />.</summary>
    public IReadOnlyList<IAuthVerifier>? AuthVerifiers { get; init; }

    /// <summary>Optional override for the message registry; falls back to <see cref="MessageTypeRegistry.CoreCatalog" />.</summary>
    public MessageTypeRegistry? MessageRegistry { get; init; }

    /// <summary>Optional extension registry for non-core message types.</summary>
    public ExtensionRegistry? ExtensionRegistry { get; init; }

    /// <summary>
    /// Optional handler invoked for any post-handshake envelope that is not
    /// natively answered by the runtime. Phase 3 wires <c>tool.invoke</c>,
    /// <c>cancel</c>, and <c>interrupt</c> through <see cref="JobManager" />
    /// directly; this handler is the catch-all for envelopes not yet covered.
    /// </summary>
    public AuthenticatedHandler? Handler { get; init; }

    /// <summary>
    /// Optional <see cref="Runtime.JobManager" />; when set, the runtime
    /// dispatches <c>tool.invoke</c>, <c>cancel</c>, and <c>interrupt</c>
    /// through it.
    /// </summary>
    public JobManager? JobManager { get; init; }

    /// <summary>
    /// Optional <see cref="Runtime.StreamManager" />; tool handlers acquire
    /// it from <see cref="ARCPRuntime" /> via the <see cref="Handler" />
    /// callback to open streams.
    /// </summary>
    public StreamManager? StreamManager { get; init; }
}

/// <summary>
/// Delegate type for an authenticated-message handler. Receives the parsed
/// envelope, the current session state, and the options used to construct
/// the runtime (so the handler can persist additional log entries, etc.).
/// </summary>
/// <param name="envelope">The inbound envelope.</param>
/// <param name="state">The active session state.</param>
/// <param name="options">The runtime configuration.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A task that completes when handling is done.</returns>
public delegate Task AuthenticatedHandler(
    Envelope.Envelope envelope,
    SessionState.Authenticated state,
    ARCPRuntimeOptions options,
    CancellationToken cancellationToken);
