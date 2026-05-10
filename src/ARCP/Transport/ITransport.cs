namespace ARCP.Transport;

/// <summary>
/// One newline-delimited / frame-delimited JSON message on the wire — the
/// transport layer treats it opaquely.
/// </summary>
/// <param name="Json">The serialized envelope JSON.</param>
public readonly record struct WireFrame(string Json);

/// <summary>
/// Transport abstraction per RFC-0001-v2 §22. A transport carries
/// <see cref="WireFrame" />s in both directions, independently of envelope
/// semantics.
/// </summary>
/// <remarks>
/// Implementations: <see cref="MemoryTransport" /> (in-process tests),
/// <c>WebSocketTransport</c> (Phase 6), <c>StdioTransport</c> (Phase 6).
/// All async methods accept <see cref="CancellationToken" /> as the last
/// parameter.
/// </remarks>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Whether the transport is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Send a single frame. Implementations <strong>SHOULD</strong> queue
    /// rather than block.
    /// </summary>
    /// <param name="frame">The frame to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the frame is enqueued.</returns>
    ValueTask SendAsync(WireFrame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive frames as an async iterator. The iterator completes when the
    /// transport is closed by either side.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async iterator of frames.</returns>
    IAsyncEnumerable<WireFrame> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Close the transport gracefully.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the transport is closed.</returns>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
