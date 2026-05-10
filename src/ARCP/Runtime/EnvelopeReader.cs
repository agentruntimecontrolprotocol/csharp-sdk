using System.Runtime.CompilerServices;
using System.Text.Json;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Transport;

namespace ARCP.Runtime;

/// <summary>
/// Bridges a <see cref="ITransport" /> to a typed
/// <see cref="IAsyncEnumerable{Envelope}" /> by parsing each
/// <see cref="WireFrame" /> through the configured serializer options.
/// </summary>
public static class EnvelopeReader
{
    /// <summary>
    /// Parse a single frame to an envelope.
    /// </summary>
    /// <param name="frame">The wire frame.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>The parsed envelope.</returns>
    /// <exception cref="InvalidArgumentException">If the frame is not valid envelope JSON.</exception>
    public static Envelope.Envelope Parse(WireFrame frame, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            Envelope.Envelope? env = JsonSerializer.Deserialize<Envelope.Envelope>(frame.Json, options);
            if (env is null)
            {
                throw new InvalidArgumentException("Wire frame deserialized to a null envelope.");
            }
            return env;
        }
        catch (JsonException ex)
        {
            throw new InvalidArgumentException(
                $"Wire frame is not a valid ARCP envelope: {ex.Message}",
                ex);
        }
    }

    /// <summary>Encode an envelope back to a frame.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>The wire frame.</returns>
    public static WireFrame Encode(Envelope.Envelope envelope, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(options);
        return new WireFrame(JsonSerializer.Serialize(envelope, options));
    }

    /// <summary>
    /// Translate a transport's wire-frame iterator into a typed envelope
    /// iterator.
    /// </summary>
    /// <param name="transport">The transport.</param>
    /// <param name="options">Serializer options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async iterator of envelopes.</returns>
    public static async IAsyncEnumerable<Envelope.Envelope> ReceiveAsync(
        ITransport transport,
        JsonSerializerOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        await foreach (WireFrame frame in transport.ReceiveAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return Parse(frame, options);
        }
    }
}
