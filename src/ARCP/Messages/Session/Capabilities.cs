using System.Text.Json.Serialization;

namespace ARCP.Messages.Session;

/// <summary>
/// Capability advertisement per RFC-0001-v2 §7. Booleans default to
/// <see langword="false" /> (a capability that is not advertised is not
/// supported).
/// </summary>
public sealed record Capabilities
{
    /// <summary>Whether streaming is supported.</summary>
    public bool? Streaming { get; init; }

    /// <summary>Whether durable jobs are supported.</summary>
    public bool? DurableJobs { get; init; }

    /// <summary>Whether checkpoint snapshots are supported.</summary>
    public bool? Checkpoints { get; init; }

    /// <summary>Whether binary streams are supported.</summary>
    public bool? BinaryStreams { get; init; }

    /// <summary>Supported binary encodings (<c>base64</c>, <c>sidecar</c>) per §11.3.</summary>
    public IReadOnlyList<string>? BinaryEncoding { get; init; }

    /// <summary>Whether multi-agent handoff is supported.</summary>
    public bool? AgentHandoff { get; init; }

    /// <summary>Whether human-in-the-loop is supported (§12).</summary>
    public bool? HumanInput { get; init; }

    /// <summary>Whether artifacts are supported (§16).</summary>
    public bool? Artifacts { get; init; }

    /// <summary>Whether subscriptions are supported (§13).</summary>
    public bool? Subscriptions { get; init; }

    /// <summary>Whether scheduled jobs are supported (§10.6).</summary>
    public bool? ScheduledJobs { get; init; }

    /// <summary>Whether <c>interrupt</c> is honored (§10.5).</summary>
    public bool? Interrupt { get; init; }

    /// <summary>Whether <c>auth.scheme: "none"</c> is permitted (§4.6).</summary>
    public bool? Anonymous { get; init; }

    /// <summary>Maximum heartbeat interval in seconds (default 30 per §10.3).</summary>
    public int? HeartbeatIntervalSeconds { get; init; }

    /// <summary>Behavior on heartbeat-interval miss (<c>fail</c> or <c>block</c>) per §10.3.</summary>
    public string? HeartbeatRecovery { get; init; }

    /// <summary>Artifact retention policy per §16.3.</summary>
    public ArtifactRetentionPolicy? ArtifactRetention { get; init; }

    /// <summary>Advertised extension namespaces per §21.2.</summary>
    public IReadOnlyList<string>? Extensions { get; init; }

    /// <summary>
    /// Open-ended additional booleans/values that don't have first-class
    /// fields — a JSON-element bag preserved across round-trips.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, System.Text.Json.JsonElement>? Additional { get; init; }
}

/// <summary>§16.3 artifact retention policy.</summary>
/// <param name="DefaultSeconds">Default retention.</param>
/// <param name="MaxSeconds">Hard upper bound on retention.</param>
public sealed record ArtifactRetentionPolicy(
    int DefaultSeconds,
    int MaxSeconds);

/// <summary>§8.2 client identity block.</summary>
/// <param name="Kind">Logical client kind (e.g. <c>"example-client"</c>).</param>
/// <param name="Version">Client version.</param>
/// <param name="Fingerprint">Optional cryptographic fingerprint (mTLS-required).</param>
/// <param name="Principal">Optional logical principal name (e.g. user email).</param>
public sealed record ClientIdentity(
    string Kind,
    string Version,
    string? Fingerprint = null,
    string? Principal = null);

/// <summary>§8.3 runtime identity block.</summary>
/// <param name="Kind">Runtime kind name.</param>
/// <param name="Version">Runtime version.</param>
/// <param name="Fingerprint">Optional cryptographic fingerprint.</param>
/// <param name="TrustLevel">Optional trust classification (§15.3).</param>
public sealed record RuntimeIdentity(
    string Kind,
    string Version,
    string? Fingerprint = null,
    TrustLevel? TrustLevel = null);
