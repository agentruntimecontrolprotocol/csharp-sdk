using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace ARCP.Transport;

/// <summary>
/// WebSocket transport per RFC-0001-v2 §22. Wraps either a
/// <see cref="ClientWebSocket" /> on the connecting side or a
/// <see cref="WebSocket" /> accepted by an ASP.NET Core endpoint on the
/// runtime side.
/// </summary>
/// <remarks>
/// v0.1 only carries text frames — sidecar binary frames are deferred per
/// PLAN.md §6. The receive loop reassembles fragmented messages into a
/// single <see cref="WireFrame" /> per envelope.
/// </remarks>
public sealed class WebSocketTransport : ITransport
{
    private readonly WebSocket _socket;
    private readonly bool _ownsSocket;
    private readonly Channel<WireFrame> _outbound;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writeLoop;
    private bool _disposed;

    /// <summary>
    /// Wrap an existing connected <see cref="WebSocket" />.
    /// </summary>
    /// <param name="socket">Connected WebSocket.</param>
    /// <param name="ownsSocket">Whether to dispose the socket when the transport is disposed.</param>
    public WebSocketTransport(WebSocket socket, bool ownsSocket = true)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _ownsSocket = ownsSocket;
        _outbound = Channel.CreateUnbounded<WireFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _writeLoop = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// Connect to a remote ARCP runtime over WebSocket.
    /// </summary>
    /// <param name="uri">The runtime URI (typically <c>ws://host:port/arcp</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A connected transport.</returns>
    public static async Task<WebSocketTransport> ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ClientWebSocket client = new();
        try
        {
            await client.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        return new WebSocketTransport(client, ownsSocket: true);
    }

    /// <inheritdoc />
    public bool IsConnected =>
        !_disposed
        && _socket.State is WebSocketState.Open or WebSocketState.CloseSent;

    /// <inheritdoc />
    public ValueTask SendAsync(WireFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _outbound.Writer.WriteAsync(frame, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WireFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
        byte[] buffer = new byte[4096];
        StringBuilder accumulator = new();

        while (!linked.Token.IsCancellationRequested
               && _socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), linked.Token).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                yield break;
            }
            catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
            {
                yield break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                yield break;
            }

            accumulator.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                string text = accumulator.ToString();
                accumulator.Clear();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new WireFrame(text);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        _disposed = true;
        _outbound.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _writeLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        if (_ownsSocket)
        {
            _socket.Dispose();
        }
        _shutdown.Dispose();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (WireFrame frame in _outbound.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
                {
                    break;
                }
                ReadOnlyMemory<byte> bytes = Encoding.UTF8.GetBytes(frame.Json);
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (WebSocketException)
        {
            // remote disconnected
        }
    }
}
