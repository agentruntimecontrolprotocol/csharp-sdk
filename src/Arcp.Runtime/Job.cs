// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Agents;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Wire;
using Arcp.Runtime.Budget;
using Arcp.Runtime.Credentials;

namespace Arcp.Runtime;

/// <summary>Runtime-side state for a running job. Owns its event emission, budget ledger, and
/// per-job cancellation linked to lease expiry (spec §7.1, §9.5).</summary>
public sealed class Job
{
    private long _nextChunkSeq;
    private readonly object _credentialGate = new();
    private readonly List<IssuedCredential> _credentials = [];
    private readonly object _eventBufferGate = new();
    private readonly List<Envelope> _eventBuffer = [];
    private readonly int _eventBufferCapacity;
    private long _lastEmittedSeq;

    /// <summary>Gets the job id.</summary>
    public JobId JobId { get; }

    /// <summary>Gets the session id.</summary>
    public SessionId SessionId { get; }

    /// <summary>Gets the agent.</summary>
    public AgentRef Agent { get; }

    /// <summary>Gets the lease.</summary>
    public Lease Lease { get; }

    /// <summary>Gets the lease constraints.</summary>
    public LeaseConstraints? LeaseConstraints { get; }

    /// <summary>Gets the input.</summary>
    public JsonElement? Input { get; }

    /// <summary>Gets the idempotency key.</summary>
    public string? IdempotencyKey { get; }

    /// <summary>Gets the trace id.</summary>
    public TraceId? TraceId { get; }

    /// <summary>Gets the parent job id.</summary>
    public string? ParentJobId { get; }

    /// <summary>Gets the submitter principal.</summary>
    public string? SubmitterPrincipal { get; }

    /// <summary>Gets the max runtime sec.</summary>
    public int? MaxRuntimeSec { get; }

    /// <summary>Gets the created at.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the budget ledger.</summary>
    public BudgetLedger BudgetLedger { get; } = new();

    /// <summary>Gets the cancellation source.</summary>
    public CancellationTokenSource CancellationSource { get; }

    /// <summary>Gets the cancellation token.</summary>
    public CancellationToken CancellationToken => CancellationSource.Token;

    /// <summary>Gets the status.</summary>
    public JobStatus Status { get; private set; } = JobStatus.Pending;

    /// <summary>Gets the streamed result id.</summary>
    public ResultId? StreamedResultId { get; private set; }

    /// <summary>Gets the streamed result size.</summary>
    public long StreamedResultSize { get; private set; }

    /// <summary>Gets the inline result emitted.</summary>
    public bool InlineResultEmitted { get; private set; }

    /// <summary>Set by the runtime watchdog when <c>max_runtime_sec</c> is exceeded so the run-loop
    /// can surface <c>TIMEOUT</c> instead of <c>CANCELLED</c>.</summary>
    public bool RuntimeLimitExceeded { get; private set; }

    internal void MarkRuntimeLimitExceeded() => RuntimeLimitExceeded = true;

    /// <summary>Set by the lease watchdog when <c>lease_constraints.expires_at</c> elapses so the
    /// run-loop can surface <c>LEASE_EXPIRED</c> / <c>final_status:"error"</c> instead of
    /// <c>CANCELLED</c> (spec §9.5).</summary>
    public bool LeaseExpired { get; private set; }

    internal void MarkLeaseExpired() => LeaseExpired = true;

    /// <summary>Gets the credentials.</summary>
    public IReadOnlyList<ProvisionedCredential> Credentials
    {
        get
        {
            lock (_credentialGate)
            {
                return _credentials.Select(c => c.Wire).ToArray();
            }
        }
    }

    private readonly Func<Envelope, CancellationToken, ValueTask> _emit;
    private readonly TimeProvider _time;

    internal Job(JobId jobId, SessionId sessionId, AgentRef agent, Lease lease, LeaseConstraints? constraints,
               JsonElement? input, string? idempotencyKey, TraceId? traceId, string? parentJobId,
               string? submitterPrincipal, int? maxRuntimeSec, DateTimeOffset createdAt,
               Func<Envelope, CancellationToken, ValueTask> emit, TimeProvider time,
               CancellationToken parentCancellation,
               int eventBufferCapacity = 4096)
    {
        JobId = jobId;
        SessionId = sessionId;
        Agent = agent;
        Lease = lease;
        LeaseConstraints = constraints;
        Input = input;
        IdempotencyKey = idempotencyKey;
        TraceId = traceId;
        ParentJobId = parentJobId;
        SubmitterPrincipal = submitterPrincipal;
        MaxRuntimeSec = maxRuntimeSec;
        CreatedAt = createdAt;
        _emit = emit;
        _time = time;
        _eventBufferCapacity = eventBufferCapacity > 0 ? eventBufferCapacity : 4096;
        CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        BudgetLedger.Initialize(lease);
    }

    /// <summary>Mark running.</summary>
    public void MarkRunning() => Status = JobStatus.Running;

    /// <summary>Mark terminal.</summary>
    public void MarkTerminal(JobStatus terminal) => Status = terminal;

    internal void SetCredentials(IReadOnlyList<IssuedCredential> credentials)
    {
        lock (_credentialGate)
        {
            _credentials.Clear();
            _credentials.AddRange(credentials);
        }
    }

    internal IssuedCredential? ReplaceCredential(string credentialId, IssuedCredential next)
    {
        lock (_credentialGate)
        {
            var index = _credentials.FindIndex(c => string.Equals(c.Wire.Id, credentialId, StringComparison.Ordinal));
            if (index < 0)
            {
                _credentials.Add(next);
                return null;
            }

            var old = _credentials[index];
            _credentials[index] = next;
            return old;
        }
    }

    /// <summary>Build the next <c>job.event</c> envelope and dispatch.</summary>
    public async ValueTask EmitEventAsync(string kind, object body, CancellationToken cancellationToken)
    {
        var payload = new JobEventPayload
        {
            Kind = kind,
            Ts = _time.GetUtcNow(),
            Body = ArcpJson.ToJsonElement(body),
        };
        var env = new Envelope
        {
            Type = MessageTypeNames.JobEvent,
            SessionId = SessionId.Value,
            JobId = JobId.Value,
            TraceId = TraceId?.Value,
            Payload = payload,
        };
        BufferEvent(env);
        await _emit(env, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Highest <c>event_seq</c> high-water mark emitted by this job, or <see langword="null"/>
    /// if it has not emitted any events yet. Surfaced in <c>session.jobs</c> (spec §6.6) so a dashboard
    /// can decide where to subscribe from.</summary>
    internal long? LastEmittedSeq
    {
        get
        {
            var seq = Interlocked.Read(ref _lastEmittedSeq);
            return seq > 0 ? seq : null;
        }
    }

    private void BufferEvent(Envelope env)
    {
        // Spec §7.6: bounded per-job history so a later subscriber with `history: true`
        // can receive prior events in order before live events. Sized from
        // `ArcpServerOptions.EventLogCapacity` so subscribers see the same window resumers do.
        lock (_eventBufferGate)
        {
            _eventBuffer.Add(env);
            if (_eventBuffer.Count > _eventBufferCapacity)
            {
                _eventBuffer.RemoveRange(0, _eventBuffer.Count - _eventBufferCapacity);
            }
        }

        // Spec §6.6: track a monotonic per-job high-water mark for last_event_seq in the listing.
        Interlocked.Increment(ref _lastEmittedSeq);
    }

    /// <summary>Snapshot of all events buffered for replay on a new subscription.</summary>
    internal IReadOnlyList<Envelope> SnapshotEventHistory()
    {
        lock (_eventBufferGate)
        {
            return _eventBuffer.ToArray();
        }
    }

    /// <summary>Apply the budget rule for <c>cost.*</c> metrics, then emit. Returns the exhausted
    /// currency (or <see langword="null"/> if all counters remain positive). Callers decide whether
    /// to surface the exhaustion as a <c>tool_result.error</c> or a fatal exception per spec §9.6
    /// (which SHOULDs the former).</summary>
    public async ValueTask<string?> EmitMetricAsync(MetricBody body, CancellationToken cancellationToken)
    {
        var charged = BudgetLedger.ApplyMetric(body.Name, body.Value, body.Unit);
        await EmitEventAsync(EventKinds.Metric, body, cancellationToken).ConfigureAwait(false);
        if (charged && !string.IsNullOrEmpty(body.Unit) && BudgetLedger.IsExhausted(body.Unit))
        {
            return body.Unit;
        }
        return null;
    }

    /// <summary>Begin result stream.</summary>
    public ResultId BeginResultStream()
    {
        if (StreamedResultId is { } existing) return existing;
        if (InlineResultEmitted)
            throw new InvalidRequestException("Cannot begin a streamed result after inline result was emitted (spec §8.4).");
        var id = ResultId.New();
        StreamedResultId = id;
        return id;
    }

    /// <summary>Write chunk (asynchronous).</summary>
    public async ValueTask WriteChunkAsync(ResultId resultId, string data, string encoding, bool more, CancellationToken cancellationToken)
    {
        if (StreamedResultId is null) StreamedResultId = resultId;
        if (!string.Equals(StreamedResultId.Value.Value, resultId.Value, StringComparison.Ordinal))
            throw new InvalidRequestException("result_id mismatch within job (spec §8.4).");
        if (encoding != "utf8" && encoding != "base64")
            throw new InvalidRequestException($"Invalid result_chunk encoding '{encoding}' (spec §8.4).");

        var seq = _nextChunkSeq++;
        var body = new ResultChunkBody
        {
            ResultId = resultId.Value,
            ChunkSeq = seq,
            Data = data,
            Encoding = encoding,
            More = more,
        };
        StreamedResultSize += encoding == "utf8" ? System.Text.Encoding.UTF8.GetByteCount(data) : (long)(data.Length * 0.75);
        await EmitEventAsync(EventKinds.ResultChunk, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Mark inline result.</summary>
    public void MarkInlineResult() => InlineResultEmitted = true;
}

/// <summary>Gets the job status.</summary>
public enum JobStatus
{
    /// <summary>Gets the pending.</summary>
    Pending,
    /// <summary>Gets the running.</summary>
    Running,
    /// <summary>Gets the success.</summary>
    Success,
    /// <summary>Gets the error.</summary>
    Error,
    /// <summary>Gets the cancelled.</summary>
    Cancelled,
    /// <summary>Gets the timed out.</summary>
    TimedOut,
}
