using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ARCP.Transport;

/// <summary>
/// Newline-delimited JSON transport over a paired
/// <see cref="TextReader" /> / <see cref="TextWriter" /> per RFC-0001-v2 §22.
/// </summary>
/// <remarks>
/// In production, both ends are <see cref="Console.In" /> / <see cref="Console.Out" />;
/// in tests, both ends are <see cref="System.IO.Pipes.AnonymousPipeServerStream" />
/// or <see cref="System.IO.StringReader" /> / <see cref="System.IO.StringWriter" />
/// pairs.
/// </remarks>
public sealed class StdioTransport : ITransport
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly Channel<WireFrame> _outbound;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writeLoop;
    private bool _disposed;

    /// <summary>
    /// Construct from existing reader/writer pair (e.g. Console.In/Console.Out
    /// or test pipes).
    /// </summary>
    /// <param name="reader">Inbound reader.</param>
    /// <param name="writer">Outbound writer.</param>
    public StdioTransport(TextReader reader, TextWriter writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _outbound = Channel.CreateUnbounded<WireFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _writeLoop = Task.Run(WriteLoopAsync);
    }

    /// <summary>Construct from <see cref="Console.In" /> and <see cref="Console.Out" />.</summary>
    /// <returns>A new <see cref="StdioTransport" /> connected to the process's stdio.</returns>
    public static StdioTransport CreateForConsole() => new(Console.In, Console.Out);

    /// <inheritdoc />
    public bool IsConnected => !_disposed;

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
        while (!linked.Token.IsCancellationRequested)
        {
            string? line = await _reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            yield return new WireFrame(line);
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
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (WireFrame frame in _outbound.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await _writer.WriteLineAsync(frame.Json.AsMemory(), _shutdown.Token).ConfigureAwait(false);
                await _writer.FlushAsync(_shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
