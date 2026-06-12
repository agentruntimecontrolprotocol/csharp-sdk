// SPDX-License-Identifier: Apache-2.0
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Client;

/// <summary>A handle to a submitted ARCP job — exposes events, chunks, and a terminal result.</summary>
public sealed class JobHandle : IAsyncDisposable
{
    private readonly ArcpClient _client;
    private readonly Channel<Envelope> _events;
    private readonly TaskCompletionSource<JobResult> _terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<JobAcceptedPayload> _accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal JobHandle(ArcpClient client)
    {
        _client = client;
        _events = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });
    }

    /// <summary>Gets the job id.</summary>
    public JobId JobId { get; internal set; }

    /// <summary>Gets the agent.</summary>
    public string Agent { get; internal set; } = string.Empty;

    /// <summary>Gets the lease.</summary>
    public Lease? Lease { get; internal set; }

    /// <summary>Gets the lease constraints.</summary>
    public LeaseConstraints? LeaseConstraints { get; internal set; }

    /// <summary>Gets the budget.</summary>
    public IReadOnlyDictionary<string, decimal>? Budget { get; internal set; }

    /// <summary>Gets the trace id.</summary>
    public TraceId? TraceId { get; internal set; }

    /// <summary>Gets the accepted.</summary>
    public Task<JobAcceptedPayload> Accepted => _accepted.Task;

    /// <summary>Gets the result.</summary>
    public Task<JobResult> Result => _terminal.Task;

    internal void OnAccepted(JobAcceptedPayload payload)
    {
        if (JobId.TryParse(payload.JobId, null, out var jid)) JobId = jid;
        Agent = payload.Agent;
        Lease = payload.Lease;
        LeaseConstraints = payload.LeaseConstraints;
        Budget = payload.Budget;
        if (!string.IsNullOrEmpty(payload.TraceId) && Arcp.Core.Ids.TraceId.TryParse(payload.TraceId, null, out var t)) TraceId = t;
        _accepted.TrySetResult(payload);
    }

    internal void OnEvent(Envelope env) => _events.Writer.TryWrite(env);

    internal void OnResult(JobResultPayload payload)
    {
        _terminal.TrySetResult(new JobResult(true, payload, null));
        _events.Writer.TryComplete();
    }

    internal void OnError(JobErrorPayload payload)
    {
        // If the job was rejected before acceptance (e.g. a server session.error for a duplicate
        // key or unavailable agent), the awaiter on Accepted must fault rather than hang forever.
        // For a post-acceptance terminal error, Accepted is already resolved so this is a no-op.
        _accepted.TrySetException(ToException(payload.Code, payload.Message, payload.Detail));
        _terminal.TrySetResult(new JobResult(false, null, payload));
        _events.Writer.TryComplete();
    }

    /// <summary>Map a wire error code to the most specific <see cref="ArcpException"/> subtype so
    /// callers can <c>catch</c> on the concrete type (e.g. <see cref="DuplicateKeyException"/>).</summary>
    internal static ArcpException ToException(string code, string message, string? detail) => code switch
    {
        ErrorCode.DuplicateKey => new DuplicateKeyException(message, detail),
        ErrorCode.AgentNotAvailable => new AgentNotAvailableException(message, detail),
        ErrorCode.AgentVersionNotAvailable => new AgentVersionNotAvailableException(message, detail),
        ErrorCode.LeaseSubsetViolation => new LeaseSubsetViolationException(message, detail),
        ErrorCode.PermissionDenied => new PermissionDeniedException(message, detail),
        ErrorCode.JobNotFound => new JobNotFoundException(message, detail),
        ErrorCode.InvalidRequest => new InvalidRequestException(message, detail),
        ErrorCode.Unauthenticated => new UnauthenticatedException(message, detail),
        ErrorCode.BudgetExhausted => new BudgetExhaustedException(message, detail),
        ErrorCode.LeaseExpired => new LeaseExpiredException(message, detail),
        ErrorCode.ResumeWindowExpired => new ResumeWindowExpiredException(message, detail),
        ErrorCode.HeartbeatLost => new HeartbeatLostException(message, detail),
        ErrorCode.Timeout => new Arcp.Core.Errors.TimeoutException(message, detail),
        ErrorCode.Cancelled => new CancelledException(message, detail),
        _ => new ArcpException(code, message, detail),
    };

    /// <summary>Events.</summary>
    public async IAsyncEnumerable<JobEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var env in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return JobEvent.From(env);
        }
    }

    /// <summary>Yield <c>result_chunk</c> events for this job, reassembled (spec §8.4).</summary>
    public async IAsyncEnumerable<ResultChunk> Chunks([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var ev in Events(cancellationToken).ConfigureAwait(false))
        {
            if (ev.Kind != EventKinds.ResultChunk) continue;
            var body = ev.BodyAs<ResultChunkBody>()!;
            yield return new ResultChunk(body);
            if (!body.More) yield break;
        }
    }

    /// <summary>Cancel (asynchronous).</summary>
    public async Task CancelAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        await _client.CancelJobAsync(JobId, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Dispose (asynchronous).</summary>
    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The terminal outcome of a job, exposing either <c>job.result</c> or <c>job.error</c>.</summary>
public sealed record JobResult(bool Success, JobResultPayload? Result, JobErrorPayload? Error)
{
    /// <summary>Gets the final status.</summary>
    public string FinalStatus => Success ? (Result?.FinalStatus ?? "success") : (Error?.FinalStatus ?? "error");

    /// <summary>Ensure success.</summary>
    public void EnsureSuccess()
    {
        if (Success) return;
        var err = Error!;
        throw new ArcpException(err.Code, err.Message, err.Detail);
    }
}

/// <summary>One decoded <c>result_chunk</c> event (spec §8.4).</summary>
public sealed record ResultChunk(ResultChunkBody Body)
{
    /// <summary>Gets the result id.</summary>
    public ResultId ResultId => new(Body.ResultId);

    /// <summary>Gets the chunk seq.</summary>
    public long ChunkSeq => Body.ChunkSeq;

    /// <summary>Gets the more.</summary>
    public bool More => Body.More;

    /// <summary>Gets the encoding.</summary>
    public string Encoding => Body.Encoding;

    /// <summary>Gets the decoded bytes.</summary>
    public byte[] DecodedBytes => Body.Encoding == "base64" ? Convert.FromBase64String(Body.Data) : System.Text.Encoding.UTF8.GetBytes(Body.Data);

    /// <summary>Gets the decoded string.</summary>
    public string DecodedString => Body.Encoding == "utf8" ? Body.Data : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Body.Data));
}
