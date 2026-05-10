using System.Collections.Concurrent;
using System.Threading.Channels;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Control;
using ARCP.Messages.Streaming;

namespace ARCP.Runtime;

/// <summary>
/// Producer-side handle on a stream. Backed by a bounded
/// <see cref="Channel{T}" />; <see cref="WriteAsync" /> blocks naturally
/// when the consumer falls behind, satisfying §11.2 backpressure.
/// </summary>
public sealed class StreamWriter : IAsyncDisposable
{
    private readonly StreamManager _manager;
    private readonly StreamRecord _record;
    private bool _closed;

    internal StreamWriter(StreamManager manager, StreamRecord record)
    {
        _manager = manager;
        _record = record;
    }

    /// <summary>The stream id.</summary>
    public StreamId Id => _record.Id;

    /// <summary>
    /// Write a chunk. Backpressure is signaled to the runtime when the bounded
    /// channel reaches the configured fill threshold.
    /// </summary>
    /// <param name="payload">The chunk to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the chunk is queued.</returns>
    public async ValueTask WriteAsync(StreamChunk payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (_closed)
        {
            throw new FailedPreconditionException($"Stream {_record.Id} is closed.");
        }

        long sequence = Interlocked.Increment(ref _record.SequenceCounter) - 1;
        StreamChunk withSeq = payload with { Sequence = sequence };
        await _manager.EmitChunkAsync(_record, withSeq, cancellationToken).ConfigureAwait(false);
        await _record.Channel.Writer.WriteAsync(withSeq, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Close the stream cleanly with an optional reason and total chunk count.
    /// </summary>
    /// <param name="reason">Optional reason text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when <c>stream.close</c> is queued.</returns>
    public async Task CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        long total = Interlocked.Read(ref _record.SequenceCounter);
        await _manager.EmitCloseAsync(_record, new StreamClose(reason, total), cancellationToken).ConfigureAwait(false);
        _record.Channel.Writer.TryComplete();
    }

    /// <summary>
    /// Close the stream with an error.
    /// </summary>
    /// <param name="error">Error envelope payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when <c>stream.error</c> is queued.</returns>
    public async Task ErrorAsync(StreamError error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_closed)
        {
            return;
        }
        _closed = true;
        await _manager.EmitErrorAsync(_record, error, cancellationToken).ConfigureAwait(false);
        _record.Channel.Writer.TryComplete(new ARCPException(error.Code, error.Message));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}

/// <summary>Internal stream record.</summary>
internal sealed class StreamRecord
{
    public required StreamId Id { get; init; }

    public required Ids.SessionId SessionId { get; init; }

    public required StreamKind Kind { get; init; }

    public required Channel<StreamChunk> Channel { get; init; }

#pragma warning disable SA1401 // Field is internal-only; Interlocked APIs require a field, not a property.
    public long SequenceCounter;
#pragma warning restore SA1401

    public bool BackpressureSignaled { get; set; }
}

/// <summary>
/// Manages live streams per RFC-0001-v2 §11. Each stream is a bounded
/// <see cref="Channel{T}" />; producers block when the consumer falls
/// behind, and the runtime emits a <c>backpressure</c> envelope at the
/// configured fill threshold.
/// </summary>
public sealed class StreamManager : IAsyncDisposable
{
    private readonly Func<Envelope.Envelope, CancellationToken, ValueTask> _emit;
    private readonly TimeProvider _time;
    private readonly int _capacity;
    private readonly double _backpressureFillRatio;
    private readonly ConcurrentDictionary<StreamId, StreamRecord> _streams = new();

    /// <summary>Initializes a new <see cref="StreamManager" />.</summary>
    /// <param name="emit">Outbound envelope writer.</param>
    /// <param name="capacity">Bounded-channel capacity per stream.</param>
    /// <param name="backpressureFillRatio">Emit a <c>backpressure</c> envelope when the channel fills past this ratio.</param>
    /// <param name="time">Time provider.</param>
    public StreamManager(
        Func<Envelope.Envelope, CancellationToken, ValueTask> emit,
        int capacity = 256,
        double backpressureFillRatio = 0.75,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(emit);
        _emit = emit;
        _time = time ?? TimeProvider.System;
        _capacity = capacity;
        _backpressureFillRatio = backpressureFillRatio;
    }

    /// <summary>
    /// Open a new stream and return the producer-side <see cref="StreamWriter" />.
    /// </summary>
    /// <param name="sessionId">Session id.</param>
    /// <param name="kind">Stream kind.</param>
    /// <param name="contentType">Optional content type.</param>
    /// <param name="encoding">Optional encoding.</param>
    /// <param name="relatedJobId">Optional related job id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A writer.</returns>
    public async ValueTask<StreamWriter> OpenAsync(
        Ids.SessionId sessionId,
        StreamKind kind,
        string? contentType = null,
        string? encoding = null,
        JobId? relatedJobId = null,
        CancellationToken cancellationToken = default)
    {
        StreamId id = StreamId.New();
        Channel<StreamChunk> channel = Channel.CreateBounded<StreamChunk>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        var record = new StreamRecord { Id = id, SessionId = sessionId, Kind = kind, Channel = channel };
        _streams[id] = record;

        await EmitOpenAsync(record, kind, contentType, encoding, relatedJobId, cancellationToken).ConfigureAwait(false);
        return new StreamWriter(this, record);
    }

    /// <summary>
    /// Iterate received chunks for an opened stream — useful for tests that
    /// observe the wire output.
    /// </summary>
    /// <param name="id">Stream id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async iterator of chunks.</returns>
    public async IAsyncEnumerable<StreamChunk> ReadAsync(
        StreamId id,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_streams.TryGetValue(id, out StreamRecord? record))
        {
            yield break;
        }
        await foreach (StreamChunk chunk in record.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    internal async ValueTask EmitChunkAsync(StreamRecord record, StreamChunk chunk, CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "stream.chunk",
            Timestamp = _time.GetUtcNow(),
            Payload = chunk,
            SessionId = record.SessionId,
            StreamId = record.Id,
        };
        await _emit(env, cancellationToken).ConfigureAwait(false);

        // Backpressure signaling.
        int approxFill = record.Channel.Reader.Count;
        if (!record.BackpressureSignaled && approxFill >= (int)(_capacity * _backpressureFillRatio))
        {
            record.BackpressureSignaled = true;
            await EmitBackpressureAsync(record, approxFill, cancellationToken).ConfigureAwait(false);
        }
        else if (record.BackpressureSignaled && approxFill < (int)(_capacity * _backpressureFillRatio * 0.5))
        {
            record.BackpressureSignaled = false;
        }
    }

    internal async ValueTask EmitCloseAsync(StreamRecord record, StreamClose close, CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "stream.close",
            Timestamp = _time.GetUtcNow(),
            Payload = close,
            SessionId = record.SessionId,
            StreamId = record.Id,
        };
        await _emit(env, cancellationToken).ConfigureAwait(false);
        _streams.TryRemove(record.Id, out _);
    }

    internal async ValueTask EmitErrorAsync(StreamRecord record, StreamError error, CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "stream.error",
            Timestamp = _time.GetUtcNow(),
            Payload = error,
            SessionId = record.SessionId,
            StreamId = record.Id,
        };
        await _emit(env, cancellationToken).ConfigureAwait(false);
        _streams.TryRemove(record.Id, out _);
    }

    private async ValueTask EmitOpenAsync(
        StreamRecord record,
        StreamKind kind,
        string? contentType,
        string? encoding,
        JobId? relatedJobId,
        CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "stream.open",
            Timestamp = _time.GetUtcNow(),
            Payload = new StreamOpen(kind, contentType, encoding, relatedJobId),
            SessionId = record.SessionId,
            StreamId = record.Id,
        };
        await _emit(env, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EmitBackpressureAsync(StreamRecord record, int approxFill, CancellationToken cancellationToken)
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "backpressure",
            Timestamp = _time.GetUtcNow(),
            Payload = new Backpressure(
                BufferRemainingBytes: Math.Max(0, _capacity - approxFill),
                Reason: "stream_buffer_threshold"),
            SessionId = record.SessionId,
            StreamId = record.Id,
        };
        await _emit(env, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (StreamRecord record in _streams.Values)
        {
            record.Channel.Writer.TryComplete();
        }
        _streams.Clear();
        return ValueTask.CompletedTask;
    }
}
