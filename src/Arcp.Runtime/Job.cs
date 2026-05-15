// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Agents;
using Arcp.Core.Wire;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Runtime.Budget;

namespace Arcp.Runtime;

/// <summary>Runtime-side state for a running job. Owns its event emission, budget ledger, and
/// per-job cancellation linked to lease expiry (spec §7.1, §9.5).</summary>
public sealed class Job
{
    private long _nextChunkSeq;

    public JobId JobId { get; }

    public SessionId SessionId { get; }

    public AgentRef Agent { get; }

    public Lease Lease { get; }

    public LeaseConstraints? LeaseConstraints { get; }

    public JsonElement? Input { get; }

    public string? IdempotencyKey { get; }

    public TraceId? TraceId { get; }

    public string? ParentJobId { get; }

    public string? SubmitterPrincipal { get; }

    public DateTimeOffset CreatedAt { get; }

    public BudgetLedger BudgetLedger { get; } = new();

    public CancellationTokenSource CancellationSource { get; }

    public CancellationToken CancellationToken => CancellationSource.Token;

    public JobStatus Status { get; private set; } = JobStatus.Pending;

    public ResultId? StreamedResultId { get; private set; }

    public long StreamedResultSize { get; private set; }

    public bool InlineResultEmitted { get; private set; }

    private readonly Func<Envelope, CancellationToken, ValueTask> _emit;
    private readonly TimeProvider _time;

    internal Job(JobId jobId, SessionId sessionId, AgentRef agent, Lease lease, LeaseConstraints? constraints,
               JsonElement? input, string? idempotencyKey, TraceId? traceId, string? parentJobId,
               string? submitterPrincipal, DateTimeOffset createdAt,
               Func<Envelope, CancellationToken, ValueTask> emit, TimeProvider time,
               CancellationToken parentCancellation)
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
        CreatedAt = createdAt;
        _emit = emit;
        _time = time;
        CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        BudgetLedger.Initialize(lease);
    }

    public void MarkRunning() => Status = JobStatus.Running;

    public void MarkTerminal(JobStatus terminal) => Status = terminal;

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
        await _emit(env, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Apply the budget rule for <c>cost.*</c> metrics, then emit. May raise
    /// <see cref="BudgetExhaustedException"/> via a follow-up <c>tool_result</c> error.</summary>
    public async ValueTask EmitMetricAsync(MetricBody body, CancellationToken cancellationToken)
    {
        BudgetLedger.ApplyMetric(body.Name, body.Value, body.Unit);
        await EmitEventAsync(EventKinds.Metric, body, cancellationToken).ConfigureAwait(false);
    }

    public ResultId BeginResultStream()
    {
        if (StreamedResultId is { } existing) return existing;
        if (InlineResultEmitted)
            throw new InvalidRequestException("Cannot begin a streamed result after inline result was emitted (spec §8.4).");
        var id = ResultId.New();
        StreamedResultId = id;
        return id;
    }

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

    public void MarkInlineResult() => InlineResultEmitted = true;
}

public enum JobStatus
{
    Pending,
    Running,
    Success,
    Error,
    Cancelled,
    TimedOut,
}
