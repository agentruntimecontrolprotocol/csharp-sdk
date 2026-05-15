// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Core.Wire;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;

namespace Arcp.Client;

/// <summary>An ARCP client. Manages one session and lets the caller submit jobs, list jobs, subscribe
/// to other jobs, and observe heartbeats/back-pressure (spec §6, §7).</summary>
public sealed class ArcpClient : IAsyncDisposable
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

        var hello = new Envelope
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
        await _transport.SendAsync(hello, cancellationToken).ConfigureAwait(false);

        var welcome = await _welcomeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (welcome is null)
            throw new InternalErrorException("No session.welcome received");
    }

    private async Task ReaderLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var env in _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                if (env.EventSeq is { } seq) Interlocked.Exchange(ref _lastReceivedSeq, seq);
                Dispatch(env, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Surface as a session error to in-flight handles.
            foreach (var h in _handles.Values)
            {
                h.OnError(new JobErrorPayload
                {
                    Code = ErrorCode.InternalError,
                    Message = "Transport closed",
                    Retryable = true,
                });
            }
        }
    }

    private void Dispatch(Envelope env, CancellationToken cancellationToken)
    {
        switch (env.Type)
        {
            case MessageTypeNames.SessionWelcome:
                if (env.Payload is SessionWelcomePayload w)
                {
                    if (env.SessionId is { } sid && SessionId.TryParse(sid, null, out var s)) SessionId = s;
                    EffectiveFeatures = FeatureSet.Intersect(_options.Features, w.Capabilities.Features);
                    ResumeToken = w.ResumeToken;
                    Agents = w.Capabilities.Agents ?? Array.Empty<AgentInventoryEntry>();
                    Runtime = w.Runtime;
                    HeartbeatIntervalSec = w.HeartbeatIntervalSec;
                    _welcomeTcs?.TrySetResult(w);
                }
                break;
            case MessageTypeNames.SessionPing:
                if (env.Payload is SessionPingPayload p)
                {
                    _ = _transport.SendAsync(new Envelope
                    {
                        Type = MessageTypeNames.SessionPong,
                        SessionId = SessionId.Value,
                        Payload = new SessionPongPayload
                        {
                            PingNonce = p.Nonce,
                            ReceivedAt = _options.TimeProvider.GetUtcNow(),
                        },
                    }, cancellationToken).AsTask();
                }
                break;
            case MessageTypeNames.SessionError:
                if (env.Payload is SessionErrorPayload err)
                {
                    foreach (var h in _handles.Values)
                    {
                        h.OnError(new JobErrorPayload
                        {
                            Code = err.Code,
                            Message = err.Message,
                            Retryable = err.Retryable,
                            Detail = err.Detail,
                        });
                    }
                }
                break;
            case MessageTypeNames.SessionJobs:
                if (env.Payload is SessionJobsPayload jobs && jobs.RequestId is { } reqId)
                {
                    if (_listJobsRequests.TryRemove(reqId, out var tcs)) tcs.TrySetResult(jobs);
                }
                break;
            case MessageTypeNames.JobAccepted:
                if (env.Payload is JobAcceptedPayload accepted && env.JobId is { } jaJid && JobId.TryParse(jaJid, null, out var jaId))
                {
                    if (_pendingSubmits.TryDequeue(out var pending))
                    {
                        pending.OnAccepted(accepted);
                        _handles[jaId] = pending;
                    }
                }
                break;
            case MessageTypeNames.JobSubscribed:
                if (env.Payload is JobSubscribedPayload subbed && env.JobId is { } jsJid && JobId.TryParse(jsJid, null, out var jsId))
                {
                    if (_subscriptions.TryGetValue(jsId, out var s2)) s2.OnSubscribed(subbed);
                }
                break;
            case MessageTypeNames.JobEvent:
                if (env.JobId is { } jeId && JobId.TryParse(jeId, null, out var jeJid))
                {
                    if (_handles.TryGetValue(jeJid, out var h2)) h2.OnEvent(env);
                    if (_subscriptions.TryGetValue(jeJid, out var sub2)) sub2.OnEvent(env);
                }
                break;
            case MessageTypeNames.JobResult:
                if (env.Payload is JobResultPayload res && env.JobId is { } jrJid && JobId.TryParse(jrJid, null, out var jrId))
                {
                    if (_handles.TryGetValue(jrId, out var h3)) h3.OnResult(res);
                    if (_subscriptions.TryGetValue(jrId, out var sub3)) sub3.OnTerminal();
                }
                break;
            case MessageTypeNames.JobError:
                if (env.Payload is JobErrorPayload jerr && env.JobId is { } jerrJid && JobId.TryParse(jerrJid, null, out var jerrId))
                {
                    if (_handles.TryGetValue(jerrId, out var h4)) h4.OnError(jerr);
                    if (_subscriptions.TryGetValue(jerrId, out var sub4)) sub4.OnTerminal();
                }
                break;
        }
    }

    public async Task<JobHandle> SubmitAsync(string agent, object? input = null, Lease? leaseRequest = null,
                                              LeaseConstraints? leaseConstraints = null, string? idempotencyKey = null,
                                              int? maxRuntimeSec = null, CancellationToken cancellationToken = default)
    {
        var handle = new JobHandle(this);
        _pendingSubmits.Enqueue(handle);
        await _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobSubmit,
            SessionId = SessionId.Value,
            Payload = new JobSubmitPayload
            {
                Agent = agent,
                Input = input is null ? null : ArcpJson.ToJsonElement(input),
                LeaseRequest = leaseRequest,
                LeaseConstraints = leaseConstraints,
                IdempotencyKey = idempotencyKey,
                MaxRuntimeSec = maxRuntimeSec,
            },
        }, cancellationToken).ConfigureAwait(false);
        await handle.Accepted.WaitAsync(cancellationToken).ConfigureAwait(false);
        return handle;
    }

    public async Task CancelJobAsync(JobId jobId, string? reason = null, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobCancel,
            SessionId = SessionId.Value,
            JobId = jobId.Value,
            Payload = new JobCancelPayload { JobId = jobId.Value, Reason = reason },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SessionJobsPayload> ListJobsAsync(JobListFilter? filter = null, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var id = "msg_" + Ulid.NewUlid();
        var tcs = new TaskCompletionSource<SessionJobsPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _listJobsRequests[id] = tcs;
        await _transport.SendAsync(new Envelope
        {
            Id = id,
            Type = MessageTypeNames.SessionListJobs,
            SessionId = SessionId.Value,
            Payload = new SessionListJobsPayload { Filter = filter, Limit = limit, Cursor = cursor },
        }, cancellationToken).ConfigureAwait(false);
        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobSubscription> SubscribeAsync(JobId jobId, bool history = false, long? fromEventSeq = null, CancellationToken cancellationToken = default)
    {
        var sub = new JobSubscription(this, jobId);
        _subscriptions[jobId] = sub;
        await _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobSubscribe,
            SessionId = SessionId.Value,
            JobId = jobId.Value,
            Payload = new JobSubscribePayload { JobId = jobId.Value, History = history, FromEventSeq = fromEventSeq },
        }, cancellationToken).ConfigureAwait(false);
        await sub.Acknowledged.WaitAsync(cancellationToken).ConfigureAwait(false);
        return sub;
    }

    public async Task UnsubscribeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        _subscriptions.TryRemove(jobId, out _);
        await _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobUnsubscribe,
            SessionId = SessionId.Value,
            JobId = jobId.Value,
            Payload = new JobUnsubscribePayload { JobId = jobId.Value },
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask AckAsync(long lastProcessedSeq, CancellationToken cancellationToken = default) =>
        _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionAck,
            SessionId = SessionId.Value,
            Payload = new SessionAckPayload { LastProcessedSeq = lastProcessedSeq },
        }, cancellationToken);

    public long LastReceivedSeq => Interlocked.Read(ref _lastReceivedSeq);

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
        catch { /* already closed */ }
        _cts.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

/// <summary>An active cross-session subscription to a job (spec §7.6).</summary>
public sealed class JobSubscription
{
    private readonly ArcpClient _client;
    private readonly Channel<Envelope> _events = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly TaskCompletionSource<JobSubscribedPayload> _ackTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public JobId JobId { get; }

    public Task<JobSubscribedPayload> Acknowledged => _ackTcs.Task;

    internal JobSubscription(ArcpClient client, JobId jobId)
    {
        _client = client;
        JobId = jobId;
    }

    internal void OnSubscribed(JobSubscribedPayload payload) => _ackTcs.TrySetResult(payload);

    internal void OnEvent(Envelope env) => _events.Writer.TryWrite(env);

    internal void OnTerminal() => _events.Writer.TryComplete();

    internal void HandleAcceptedFromOtherSession(JobAcceptedPayload _) { /* informational */ }

    public async IAsyncEnumerable<JobEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var env in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return JobEvent.From(env);
        }
    }

    public Task UnsubscribeAsync(CancellationToken cancellationToken = default) =>
        _client.UnsubscribeAsync(JobId, cancellationToken);
}
