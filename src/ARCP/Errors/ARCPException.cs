using System.Collections.Frozen;
using System.Text.Json;

namespace ARCP.Errors;

/// <summary>
/// Base exception for all ARCP-internal failures. Always carries a canonical
/// <see cref="ErrorCode" />; subclasses pin specific codes for ergonomic catches.
/// </summary>
/// <remarks>
/// All public APIs in <see cref="ARCP" /> throw only
/// <see cref="ARCPException" /> (or <see cref="ArgumentException" /> /
/// <see cref="ArgumentNullException" /> for caller bugs). Library exceptions
/// from <c>Microsoft.Data.Sqlite</c>, <c>Microsoft.IdentityModel</c>, and
/// <c>System.Text.Json</c> are wrapped at the boundary. See RFC-0001-v2 §18.
/// </remarks>
public class ARCPException : Exception
{
    /// <summary>Initializes a new <see cref="ARCPException" />.</summary>
    /// <param name="code">Canonical error code (§18.2).</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="retryable">Override the default retryability per §18.3.</param>
    /// <param name="details">Optional structured details.</param>
    /// <param name="cause">Optional inner exception.</param>
    /// <param name="traceId">Optional trace id for correlation.</param>
    public ARCPException(
        ErrorCode code,
        string message,
        bool? retryable = null,
        IReadOnlyDictionary<string, JsonElement>? details = null,
        Exception? cause = null,
        string? traceId = null)
        : base(message, cause)
    {
        Code = code;
        Retryable = retryable ?? ErrorCodes.IsRetryableByDefault(code);
        Details = details is null
            ? FrozenDictionary<string, JsonElement>.Empty
            : details.ToFrozenDictionary();
        TraceId = traceId;
    }

    /// <summary>Canonical error code (§18.2).</summary>
    public ErrorCode Code { get; }

    /// <summary>Whether this error is retryable. Defaults from §18.3.</summary>
    public bool Retryable { get; }

    /// <summary>Extra structured details (immutable).</summary>
    public IReadOnlyDictionary<string, JsonElement> Details { get; }

    /// <summary>Trace id for correlation, if known.</summary>
    public string? TraceId { get; }

    /// <summary>Serialize to the wire <see cref="ErrorPayload" /> shape.</summary>
    /// <returns>The wire payload.</returns>
    public ErrorPayload ToPayload()
    {
        ErrorPayload? cause = (InnerException is ARCPException inner) ? inner.ToPayload() : null;
        return new ErrorPayload
        {
            Code = Code,
            Message = Message,
            Retryable = Retryable,
            Details = Details.Count == 0 ? null : Details,
            Cause = cause,
            TraceId = TraceId,
        };
    }

    /// <summary>Re-hydrate an <see cref="ARCPException" /> from a wire payload.</summary>
    /// <param name="payload">The wire payload.</param>
    /// <returns>An exception representing the same error.</returns>
    public static ARCPException FromPayload(ErrorPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ARCPException? cause = payload.Cause is { } c ? FromPayload(c) : null;
        return new ARCPException(
            payload.Code,
            payload.Message,
            retryable: payload.Retryable,
            details: payload.Details,
            cause: cause,
            traceId: payload.TraceId);
    }
}
