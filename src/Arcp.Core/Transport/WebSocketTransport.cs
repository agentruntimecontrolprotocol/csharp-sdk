// SPDX-License-Identifier: Apache-2.0
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Core.Transport;

/// <summary>WebSocket transport (spec §4.1). UTF-8 text frames containing one JSON envelope each.</summary>
public sealed class WebSocketTransport : ITransport
{
    private readonly WebSocket _socket;
    private readonly bool _ownsSocket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _closed;

    /// <summary>Initializes a new instance of the <see cref="WebSocketTransport"/> class.</summary>
    public WebSocketTransport(WebSocket socket, bool ownsSocket = true)
    {
        _socket = socket;
        _ownsSocket = ownsSocket;
    }

    /// <summary>Send (asynchronous).</summary>
    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (_closed || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException($"WebSocket not open (state={_socket.State}).");

        var bytes = ArcpJson.SerializeUtf8(envelope);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Receive (asynchronous).</summary>
    public async IAsyncEnumerable<Envelope> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        ArrayBufferWriter<byte>? writer = null;
        try
        {
            while (!_closed && _socket.State == WebSocketState.Open)
            {
                var first = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (first.MessageType == WebSocketMessageType.Close)
                {
                    _closed = true;
                    yield break;
                }

                Envelope? env = null;
                if (first.EndOfMessage)
                {
                    // Fast path: full envelope is in the rented buffer. Deserialize directly.
                    if (first.Count == 0) continue;
                    env = TryDeserialize(buffer.AsSpan(0, first.Count));
                }
                else
                {
                    // Slow path: message spans multiple frames. Stream into a pooled buffer
                    // writer that grows as needed instead of allocating a new MemoryStream per call.
                    writer ??= new ArrayBufferWriter<byte>(16384);
                    writer.Clear();
                    if (first.Count > 0)
                    {
                        buffer.AsSpan(0, first.Count).CopyTo(writer.GetSpan(first.Count));
                        writer.Advance(first.Count);
                    }

                    var done = false;
                    while (!done)
                    {
                        var frame = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                        if (frame.MessageType == WebSocketMessageType.Close)
                        {
                            _closed = true;
                            yield break;
                        }
                        if (frame.Count > 0)
                        {
                            buffer.AsSpan(0, frame.Count).CopyTo(writer.GetSpan(frame.Count));
                            writer.Advance(frame.Count);
                        }
                        done = frame.EndOfMessage;
                    }

                    if (writer.WrittenCount == 0) continue;
                    env = TryDeserialize(writer.WrittenSpan);
                }

                if (env is not null) yield return env;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Envelope? TryDeserialize(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return ArcpJson.Deserialize(utf8);
        }
        catch (Errors.ArcpException ex)
        {
            return InvalidEnvelopeSentinel(ex.Message);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return InvalidEnvelopeSentinel(ex.Message);
        }
    }

    private static Envelope InvalidEnvelopeSentinel(string parseError) => new()
    {
        Type = MessageTypeNames.InvalidEnvelope,
        Payload = new InvalidEnvelopePayload { ParseError = parseError },
    };

    /// <summary>Close (asynchronous).</summary>
    public async ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_closed) return;
        _closed = true;
        try
        {
            if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException) { /* already closed */ }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>Dispose (asynchronous).</summary>
    public async ValueTask DisposeAsync()
    {
        try { await CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
        if (_ownsSocket) _socket.Dispose();
        _sendLock.Dispose();
    }
}
