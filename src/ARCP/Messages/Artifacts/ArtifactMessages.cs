using ARCP.Envelope;

namespace ARCP.Messages.Artifacts;

/// <summary>§16.2 upload an artifact (inline base64 in v0.1; sidecar deferred).</summary>
public sealed record ArtifactPut(
    string MediaType,
    Ids.ArtifactId? ArtifactId = null,
    string? Data = null,
    string? Encoding = null,
    int? TtlSeconds = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "artifact.put";
}

/// <summary>§16.2 fetch an artifact by id.</summary>
public sealed record ArtifactFetch(Ids.ArtifactId ArtifactId) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "artifact.fetch";
}

/// <summary>§16.1 canonical artifact reference.</summary>
public sealed record ArtifactRef(
    Ids.ArtifactId ArtifactId,
    string Uri,
    string MediaType,
    long Size,
    string? Sha256 = null,
    DateTimeOffset? ExpiresAt = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "artifact.ref";
}

/// <summary>§16.2 release a fetched artifact.</summary>
public sealed record ArtifactRelease(Ids.ArtifactId ArtifactId) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "artifact.release";
}
