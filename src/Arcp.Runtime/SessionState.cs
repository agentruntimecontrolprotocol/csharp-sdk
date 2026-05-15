// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Store;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Arcp.Runtime.Authorization;
using Arcp.Runtime.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

/// <summary>One live ARCP session on the server side. Owns the transport, the inbound dispatcher,
/// the outbound channel, the event log, and the heartbeat + ack timers (spec §6).</summary>
public sealed class SessionState : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly ArcpServer _server;
    private readonly ArcpServerOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<Envelope> _outbound;
    private readonly CancellationTokenSource _cts;

    private long _lastAckedSeq;
    private bool _heartbeatNegotiated;
    private bool _ackNegotiated;
    private DateTimeOffset _lastInboundAt;

    public SessionId SessionId { get; private set; } = SessionId.New();

    public string ResumeToken { get; private set; } = MintResumeToken();

    public AuthPrincipal? Principal { get; private set; }

    public IReadOnlyList<string> EffectiveFeatures { get; private set; } = Array.Empty<string>();

    public EventLog EventLog { get; } = new();

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
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _lastInboundAt = options.TimeProvider.GetUtcNow();
    }

    public async Task RunAsync()
    {
        var sender = Task.Run(() => SenderLoop(_cts.Token));
        var heartbeat = Task.Run(() => HeartbeatLoop(_cts.Token));
        try
        {
            await ReceiverLoop(_cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _cts.Cancel();
            _outbound.Writer.TryComplete();
            try { await sender.ConfigureAwait(false); } catch { /* shutdown */ }
            try { await heartbeat.ConfigureAwait(false); } catch { /* shutdown */ }
            IsClosed = true;
            _server.RemoveSession(this);
        }
    }

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

    private async Task SenderLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var env in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _transport.SendAsync(env, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
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
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private async Task DispatchAsync(Envelope env, CancellationToken cancellationToken)
    {
        switch (env.Type)
        {
            case MessageTypeNames.SessionHello:
                await HandleHelloAsync(env, cancellationToken).ConfigureAwait(false);
                break;
            case MessageTypeNames.SessionPing:
                if (env.Payload is SessionPingPayload p)
                {
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
                break;
            case MessageTypeNames.SessionPong:
                /* clock-skew telemetry only; no-op */
                break;
            case MessageTypeNames.SessionAck:
                if (env.Payload is SessionAckPayload ack)
                {
                    Interlocked.Exchange(ref _lastAckedSeq, ack.LastProcessedSeq);
                    EventLog.Trim(ack.LastProcessedSeq);
                    var lag = EventLog.HighWatermark - ack.LastProcessedSeq;
                    if (lag > _options.BackPressureThreshold)
                    {
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

    private async Task HandleHelloAsync(Envelope env, CancellationToken cancellationToken)
    {
        if (env.Payload is not SessionHelloPayload hello)
            throw new InvalidRequestException("session.hello payload missing");

        if (_options.Auth is { } verifier)
        {
            Principal = await verifier.VerifyAsync(hello.Auth?.Token, cancellationToken).ConfigureAwait(false);
            if (Principal is null)
                throw new UnauthenticatedException("Invalid or missing bearer token");
        }
        else
        {
            Principal = new AuthPrincipal(hello.Auth?.Token ?? "anonymous");
        }

        // Resume path (spec §6.3): if resume_token matches, keep the session_id.
        if (!string.IsNullOrEmpty(hello.ResumeToken) && _server.TryResume(hello.ResumeToken, out var resumed))
        {
            SessionId = resumed!.SessionId;
        }

        EffectiveFeatures = FeatureSet.Intersect(hello.Capabilities.Features, _options.Features);
        _heartbeatNegotiated = FeatureSet.Has(EffectiveFeatures, FeatureFlags.Heartbeat);
        _ackNegotiated = FeatureSet.Has(EffectiveFeatures, FeatureFlags.Ack);

        ResumeToken = MintResumeToken();

        var welcome = new Envelope
        {
            Type = MessageTypeNames.SessionWelcome,
            SessionId = SessionId.Value,
            Payload = new SessionWelcomePayload
            {
                Runtime = _options.Runtime,
                ResumeToken = ResumeToken,
                ResumeWindowSec = _options.ResumeWindowSec,
                HeartbeatIntervalSec = _heartbeatNegotiated ? _options.HeartbeatIntervalSec : null,
                Capabilities = new Capabilities
                {
                    Encodings = _options.Encodings ?? Array.Empty<string>(),
                    Features = _options.Features,
                    Agents = _server.AgentRegistry.ToInventory(),
                },
            },
        };
        await SendAsync(welcome, cancellationToken).ConfigureAwait(false);

        // Replay buffered events if resuming with last_event_seq (spec §6.3).
        if (hello.LastEventSeq is { } last)
        {
            foreach (var bufferedEnv in EventLog.ReadFrom(last))
            {
                await SendAsync(bufferedEnv, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleListJobsAsync(Envelope env, CancellationToken cancellationToken)
    {
        if (!FeatureSet.Has(EffectiveFeatures, FeatureFlags.ListJobs))
            throw new InvalidRequestException("'list_jobs' feature not negotiated (spec §6.2)");
        var payload = env.Payload as SessionListJobsPayload;
        var page = _server.JobManager.List(Principal?.Subject, _options.AuthorizationPolicy,
            payload?.Filter, payload?.Limit, payload?.Cursor, out var nextCursor);
        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionJobs,
            SessionId = SessionId.Value,
            Payload = new SessionJobsPayload
            {
                RequestId = env.Id,
                Jobs = page,
                NextCursor = nextCursor,
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleJobSubmitAsync(Envelope env, JobSubmitPayload submit, CancellationToken cancellationToken)
    {
        Func<Envelope, CancellationToken, ValueTask> emit = (e, ct) => EmitJobEnvelopeAsync(e, ct);

        Job job;
        JobAcceptedPayload accepted;
        try
        {
            job = _server.JobManager.Submit(submit, SessionId, Principal?.Subject, emit, _cts.Token, out accepted);
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
            return;
        }

        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobAccepted,
            SessionId = SessionId.Value,
            JobId = job.JobId.Value,
            TraceId = job.TraceId?.Value,
            Payload = accepted,
        }, cancellationToken).ConfigureAwait(false);

        // Resolve agent and run.
        var resolved = _server.AgentRegistry.Resolve(job.Agent).Agent;
        _ = Task.Run(() => _server.JobManager.RunAsync(job, resolved, emit, _cts.Token), _cts.Token);
    }

    private async Task HandleSubscribeAsync(Envelope env, JobSubscribePayload sub, CancellationToken cancellationToken)
    {
        if (!FeatureSet.Has(EffectiveFeatures, FeatureFlags.Subscribe))
            throw new InvalidRequestException("'subscribe' feature not negotiated (spec §6.2)");
        if (!JobId.TryParse(sub.JobId, null, out var jid))
            throw new InvalidRequestException("Invalid job_id");
        if (!_server.JobManager.TryGet(jid, out var job) || job is null)
            throw new JobNotFoundException($"job {sub.JobId} not found or not visible");

        if (!_options.AuthorizationPolicy.CanObserve(job.SubmitterPrincipal, Principal))
            throw new PermissionDeniedException("Subscriber not authorized to observe job");

        _server.Subscriptions.Subscribe(job.JobId, SessionId);

        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobSubscribed,
            SessionId = SessionId.Value,
            JobId = job.JobId.Value,
            Payload = new JobSubscribedPayload
            {
                JobId = job.JobId.Value,
                CurrentStatus = job.Status.ToString().ToLowerInvariant(),
                Agent = job.Agent.ToString(),
                Lease = job.Lease,
                LeaseConstraints = job.LeaseConstraints,
                ParentJobId = job.ParentJobId,
                TraceId = job.TraceId?.Value,
                SubscribedFrom = EventLog.HighWatermark,
                Replayed = sub.History,
            },
        }, cancellationToken).ConfigureAwait(false);

        // Spec §7.6 history replay (not implemented across server-internal job buffer in this MVP).
    }

    private async ValueTask EmitJobEnvelopeAsync(Envelope env, CancellationToken cancellationToken)
    {
        // Append the event to this owning session's log if it's an event/result/error.
        var stamped = env.Type is MessageTypeNames.JobEvent or MessageTypeNames.JobResult or MessageTypeNames.JobError
            ? EventLog.Append(env)
            : env;

        await SendAsync(stamped, cancellationToken).ConfigureAwait(false);

        // Fan out to subscribers (spec §7.6).
        if (env.JobId is { } jobIdStr && JobId.TryParse(jobIdStr, null, out var jid))
        {
            foreach (var sub in _server.Subscriptions.SubscribersOf(jid))
            {
                if (sub.Value == SessionId.Value) continue;
                _server.GetSession(sub)?.EmitToSubscriber(stamped, cancellationToken);
            }
        }
    }

    internal void EmitToSubscriber(Envelope env, CancellationToken cancellationToken)
    {
        // Re-stamp for the subscriber's session_id and event_seq.
        var rekeyed = env with { SessionId = SessionId.Value };
        var stamped = EventLog.Append(rekeyed);
        _outbound.Writer.TryWrite(stamped);
    }

    private async ValueTask EmitEventAsync(Envelope env, CancellationToken cancellationToken)
    {
        var stamped = EventLog.Append(env);
        await SendAsync(stamped, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SendAsync(Envelope env, CancellationToken cancellationToken = default) =>
        _outbound.Writer.WriteAsync(env, cancellationToken);

    public async ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (IsClosed) return;
        IsClosed = true;
        try
        {
            await SendAsync(new Envelope
            {
                Type = MessageTypeNames.SessionBye,
                SessionId = SessionId.Value,
                Payload = new SessionByePayload { Reason = reason },
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { /* may already be closed */ }
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

    public async ValueTask DisposeAsync()
    {
        if (IsClosed) return;
        await CloseAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
