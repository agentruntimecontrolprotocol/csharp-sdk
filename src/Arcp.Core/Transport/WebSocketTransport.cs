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
using Arcp.Core.Wire;

namespace Arcp.Core.Transport;

/// <summary>WebSocket transport (spec §4.1). UTF-8 text frames containing one JSON envelope each.</summary>
public sealed class WebSocketTransport : ITransport
{
    private readonly WebSocket _socket;
    private readonly bool _ownsSocket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _closed;

    public WebSocketTransport(WebSocket socket, bool ownsSocket = true)
    {
        _socket = socket;
        _ownsSocket = ownsSocket;
    }

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

    public async IAsyncEnumerable<Envelope> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (!_closed && _socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _closed = true;
                        break;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);
                if (_closed) yield break;

                if (ms.Length == 0) continue;
                Envelope env;
                try
                {
                    env = ArcpJson.Deserialize(ms.ToArray());
                }
                catch (Exception ex) when (ex is Errors.ArcpException or System.Text.Json.JsonException)
                {
                    continue;
                }
                yield return env;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

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

    public async ValueTask DisposeAsync()
    {
        try { await CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
        if (_ownsSocket) _socket.Dispose();
        _sendLock.Dispose();
    }
}
