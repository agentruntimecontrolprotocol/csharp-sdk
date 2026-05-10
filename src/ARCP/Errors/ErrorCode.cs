using System.Text.Json.Serialization;

namespace ARCP.Errors;

/// <summary>
/// Canonical ARCP error codes per RFC-0001-v2 §18.2. Implementations
/// <strong>MUST</strong> use these codes when applicable; deployment-specific
/// codes <strong>MUST</strong> be namespaced (e.g. <c>arcpx.acme.QUOTA_EXCEEDED</c>)
/// and are not represented in this enum.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErrorCode>))]
public enum ErrorCode
{
    /// <summary>Not an error; reserved (§18.2).</summary>
    [JsonStringEnumMemberName("OK")]
    Ok,

    /// <summary>Operation cancelled by caller, runtime, or policy.</summary>
    [JsonStringEnumMemberName("CANCELLED")]
    Cancelled,

    /// <summary>Unknown error; avoid in favor of a specific code.</summary>
    [JsonStringEnumMemberName("UNKNOWN")]
    Unknown,

    /// <summary>Caller passed a malformed or invalid argument.</summary>
    [JsonStringEnumMemberName("INVALID_ARGUMENT")]
    InvalidArgument,

    /// <summary>Operation timed out before completion.</summary>
    [JsonStringEnumMemberName("DEADLINE_EXCEEDED")]
    DeadlineExceeded,

    /// <summary>Referenced entity does not exist.</summary>
    [JsonStringEnumMemberName("NOT_FOUND")]
    NotFound,

    /// <summary>Entity creation conflicted with existing entity.</summary>
    [JsonStringEnumMemberName("ALREADY_EXISTS")]
    AlreadyExists,

    /// <summary>Caller lacks required permission or lease.</summary>
    [JsonStringEnumMemberName("PERMISSION_DENIED")]
    PermissionDenied,

    /// <summary>Quota or rate limit hit (<c>RATE_LIMITED</c> is an alias).</summary>
    [JsonStringEnumMemberName("RESOURCE_EXHAUSTED")]
    ResourceExhausted,

    /// <summary>Pre-condition unmet (e.g. job not in cancellable state).</summary>
    [JsonStringEnumMemberName("FAILED_PRECONDITION")]
    FailedPrecondition,

    /// <summary>Concurrency conflict or hard termination.</summary>
    [JsonStringEnumMemberName("ABORTED")]
    Aborted,

    /// <summary>Argument out of valid range (subset of <see cref="InvalidArgument" />).</summary>
    [JsonStringEnumMemberName("OUT_OF_RANGE")]
    OutOfRange,

    /// <summary>Feature not supported by this runtime.</summary>
    [JsonStringEnumMemberName("UNIMPLEMENTED")]
    Unimplemented,

    /// <summary>Internal runtime error.</summary>
    [JsonStringEnumMemberName("INTERNAL")]
    Internal,

    /// <summary>Transient unavailability; retry MAY succeed.</summary>
    [JsonStringEnumMemberName("UNAVAILABLE")]
    Unavailable,

    /// <summary>Unrecoverable data loss or corruption.</summary>
    [JsonStringEnumMemberName("DATA_LOSS")]
    DataLoss,

    /// <summary>Missing or invalid credentials.</summary>
    [JsonStringEnumMemberName("UNAUTHENTICATED")]
    Unauthenticated,

    /// <summary>Job missed required heartbeats (§10.3).</summary>
    [JsonStringEnumMemberName("HEARTBEAT_LOST")]
    HeartbeatLost,

    /// <summary>Operation attempted with expired lease (§15.5).</summary>
    [JsonStringEnumMemberName("LEASE_EXPIRED")]
    LeaseExpired,

    /// <summary>Operation attempted with revoked lease.</summary>
    [JsonStringEnumMemberName("LEASE_REVOKED")]
    LeaseRevoked,

    /// <summary>Subscription or stream dropped due to overflow.</summary>
    [JsonStringEnumMemberName("BACKPRESSURE_OVERFLOW")]
    BackpressureOverflow,
}

/// <summary>
/// Helpers for working with <see cref="ErrorCode" /> values.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// <c>RATE_LIMITED</c> is an alias for <see cref="ErrorCode.ResourceExhausted" />
    /// per §18.2. Exposed as a constant for clarity at call sites.
    /// </summary>
    public const ErrorCode RateLimited = ErrorCode.ResourceExhausted;

    /// <summary>
    /// Default retryability per §18.3. Codes not listed here default to
    /// non-retryable.
    /// </summary>
    /// <param name="code">The canonical error code.</param>
    /// <returns><see langword="true" /> if retryable by default per §18.3.</returns>
    public static bool IsRetryableByDefault(ErrorCode code) => code switch
    {
        ErrorCode.ResourceExhausted => true,
        ErrorCode.Unavailable => true,
        ErrorCode.DeadlineExceeded => true,
        ErrorCode.Internal => true,
        ErrorCode.Aborted => true,
        _ => false,
    };

    /// <summary>
    /// The canonical wire-form string for <paramref name="code" />, e.g.
    /// <c>"INVALID_ARGUMENT"</c>.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <returns>The wire string.</returns>
    public static string ToWireString(this ErrorCode code) => code switch
    {
        ErrorCode.Ok => "OK",
        ErrorCode.Cancelled => "CANCELLED",
        ErrorCode.Unknown => "UNKNOWN",
        ErrorCode.InvalidArgument => "INVALID_ARGUMENT",
        ErrorCode.DeadlineExceeded => "DEADLINE_EXCEEDED",
        ErrorCode.NotFound => "NOT_FOUND",
        ErrorCode.AlreadyExists => "ALREADY_EXISTS",
        ErrorCode.PermissionDenied => "PERMISSION_DENIED",
        ErrorCode.ResourceExhausted => "RESOURCE_EXHAUSTED",
        ErrorCode.FailedPrecondition => "FAILED_PRECONDITION",
        ErrorCode.Aborted => "ABORTED",
        ErrorCode.OutOfRange => "OUT_OF_RANGE",
        ErrorCode.Unimplemented => "UNIMPLEMENTED",
        ErrorCode.Internal => "INTERNAL",
        ErrorCode.Unavailable => "UNAVAILABLE",
        ErrorCode.DataLoss => "DATA_LOSS",
        ErrorCode.Unauthenticated => "UNAUTHENTICATED",
        ErrorCode.HeartbeatLost => "HEARTBEAT_LOST",
        ErrorCode.LeaseExpired => "LEASE_EXPIRED",
        ErrorCode.LeaseRevoked => "LEASE_REVOKED",
        ErrorCode.BackpressureOverflow => "BACKPRESSURE_OVERFLOW",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown ErrorCode"),
    };
}
