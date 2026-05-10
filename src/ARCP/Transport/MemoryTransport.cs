using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ARCP.Transport;

/// <summary>
/// In-process transport pair backed by two <see cref="Channel{T}" />s. Used
/// throughout the test suite to drive runtime/client interaction without a
/// real socket.
/// </summary>
/// <remarks>
/// Construct with <see cref="MemoryTransport.CreatePair" /> to get a paired
/// <c>(client, server)</c> tuple wired so each end's <see cref="SendAsync" />
/// is the other end's <see cref="ReceiveAsync" /> source.
/// </remarks>
public sealed class MemoryTransport : ITransport
{
    private readonly Channel<WireFrame> _outbound;
    private readonly Channel<WireFrame> _inbound;
    private bool _closed;

    private MemoryTransport(Channel<WireFrame> outbound, Channel<WireFrame> inbound)
    {
        _outbound = outbound;
        _inbound = inbound;
    }

    /// <inheritdoc />
    public bool IsConnected => !_closed;

    /// <summary>
    /// Create a paired set of in-process transports. The <c>client</c> end
    /// drives a runtime-side <c>server</c> end; either side can call
    /// <see cref="SendAsync" /> and the other receives via
    /// <see cref="ReceiveAsync" />.
    /// </summary>
    /// <param name="capacity">Bounded channel capacity per direction.</param>
    /// <returns>A pair of transports; the first acts as a client, the second as the runtime peer.</returns>
    public static (MemoryTransport Client, MemoryTransport Server) CreatePair(int capacity = 256)
    {
        var clientToServer = Channel.CreateBounded<WireFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        var serverToClient = Channel.CreateBounded<WireFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        var client = new MemoryTransport(outbound: clientToServer, inbound: serverToClient);
        var server = new MemoryTransport(outbound: serverToClient, inbound: clientToServer);
        return (client, server);
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(WireFrame frame, CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            throw new InvalidOperationException("MemoryTransport is closed.");
        }
        await _outbound.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WireFrame> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (WireFrame frame in _inbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return Task.CompletedTask;
        }
        _closed = true;
        _outbound.Writer.TryComplete();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }
}
