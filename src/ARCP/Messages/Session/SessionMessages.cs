using ARCP.Envelope;
using ARCP.Errors;

namespace ARCP.Messages.Session;

/// <summary>§8.1 client &gt; runtime: open a new session.</summary>
public sealed record SessionOpen(
    AuthCredential Auth,
    ClientIdentity Client,
    Capabilities Capabilities) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.open";
}

/// <summary>§8.1 runtime &gt; client: challenge requiring further proof.</summary>
public sealed record SessionChallenge(
    string ChallengeId,
    string Type,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Params = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.challenge";
}

/// <summary>§8.1 client &gt; runtime: response to a challenge.</summary>
public sealed record SessionAuthenticate(
    string ChallengeId,
    System.Text.Json.JsonElement? Response = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.authenticate";
}

/// <summary>§8.3 runtime &gt; client: session created.</summary>
public sealed record SessionAccepted(
    Ids.SessionId SessionId,
    RuntimeIdentity Runtime,
    Capabilities Capabilities,
    SessionLease? Lease = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.accepted";
}

/// <summary>Session-level lease descriptor (currently just an expiry).</summary>
/// <param name="ExpiresAt">When this session lease expires.</param>
public sealed record SessionLease(DateTimeOffset ExpiresAt);

/// <summary>§8.1 runtime &gt; client: missing/invalid credentials.</summary>
public sealed record SessionUnauthenticated(string Reason) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.unauthenticated";
}

/// <summary>§8.1 runtime &gt; client: structured rejection.</summary>
public sealed record SessionRejected : MessageType
{
    /// <summary>Canonical error code.</summary>
    public required ErrorCode Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional detail payload.</summary>
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Details { get; init; }

    /// <inheritdoc />
    public override string WireType => "session.rejected";
}

/// <summary>§8.4 runtime &gt; client: re-authentication required.</summary>
public sealed record SessionRefresh(
    string Reason,
    int DeadlineMs) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.refresh";
}

/// <summary>§8.5 runtime &gt; client: session evicted.</summary>
public sealed record SessionEvicted(
    string Reason,
    string? Message = null,
    bool? AllowResume = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.evicted";
}

/// <summary>§9 client/runtime: graceful close.</summary>
public sealed record SessionClose(
    string? Reason = null,
    string? DisposeJobs = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "session.close";
}
