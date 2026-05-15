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
using Arcp.Core.Wire;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;

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

    public JobId JobId { get; internal set; }

    public string Agent { get; internal set; } = string.Empty;

    public Lease? Lease { get; internal set; }

    public LeaseConstraints? LeaseConstraints { get; internal set; }

    public IReadOnlyDictionary<string, decimal>? Budget { get; internal set; }

    public TraceId? TraceId { get; internal set; }

    public Task<JobAcceptedPayload> Accepted => _accepted.Task;

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
        _terminal.TrySetResult(new JobResult(false, null, payload));
        _events.Writer.TryComplete();
    }

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

    public async Task CancelAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        await _client.CancelJobAsync(JobId, reason, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The terminal outcome of a job, exposing either <c>job.result</c> or <c>job.error</c>.</summary>
public sealed record JobResult(bool Success, JobResultPayload? Result, JobErrorPayload? Error)
{
    public string FinalStatus => Success ? (Result?.FinalStatus ?? "success") : (Error?.FinalStatus ?? "error");

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
    public ResultId ResultId => new(Body.ResultId);

    public long ChunkSeq => Body.ChunkSeq;

    public bool More => Body.More;

    public string Encoding => Body.Encoding;

    public byte[] DecodedBytes => Body.Encoding == "base64" ? Convert.FromBase64String(Body.Data) : System.Text.Encoding.UTF8.GetBytes(Body.Data);

    public string DecodedString => Body.Encoding == "utf8" ? Body.Data : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Body.Data));
}
