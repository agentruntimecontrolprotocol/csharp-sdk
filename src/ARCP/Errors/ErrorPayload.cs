using System.Text.Json.Serialization;

namespace ARCP.Errors;

/// <summary>
/// Wire shape of an ARCP error payload (used by <c>tool.error</c>,
/// <c>job.failed</c>, <c>nack</c>, <c>stream.error</c>, etc.) per
/// RFC-0001-v2 §18.1.
/// </summary>
public sealed record ErrorPayload
{
    /// <summary>Canonical error code (§18.2).</summary>
    [JsonPropertyName("code")]
    public required ErrorCode Code { get; init; }

    /// <summary>Human-readable message.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Whether the error is retryable. Defaults from §18.3.</summary>
    [JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    /// <summary>Additional structured detail (e.g. <c>retry_after_seconds</c>).</summary>
    [JsonPropertyName("details")]
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Details { get; init; }

    /// <summary>Chained cause, if any.</summary>
    [JsonPropertyName("cause")]
    public ErrorPayload? Cause { get; init; }

    /// <summary>Trace id for correlation, if known.</summary>
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }
}
