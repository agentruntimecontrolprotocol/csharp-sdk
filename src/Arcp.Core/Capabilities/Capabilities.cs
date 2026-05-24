// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Arcp.Core.Caps;

/// <summary>Capabilities exchanged on <c>session.hello</c> and <c>session.welcome</c> (spec §6.2).</summary>
public sealed record Capabilities
{
    /// <summary>Gets the encodings.</summary>
    [JsonPropertyName("encodings")]
    public IReadOnlyList<string> Encodings { get; init; } = new[] { "json" };

    /// <summary>Feature flags advertised by the peer. The effective set is the intersection of
    /// hello.features and welcome.features (spec §6.2).</summary>
    [JsonPropertyName("features")]
    public IReadOnlyList<string>? Features { get; init; }

    /// <summary>Agent inventory. On a v1.1 welcome this is a list of <see cref="AgentInventoryEntry"/>;
    /// on a v1.0 welcome each entry was just a name. See <see cref="AgentInventoryEntry"/>.</summary>
    [JsonPropertyName("agents")]
    public IReadOnlyList<AgentInventoryEntry>? Agents { get; init; }
}

/// <summary>An agent inventory entry: name plus available versions and a default (spec §6.2 / §7.5).</summary>
public sealed record AgentInventoryEntry
{
    /// <summary>Gets the name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets the versions.</summary>
    [JsonPropertyName("versions")]
    public IReadOnlyList<string>? Versions { get; init; }

    /// <summary>Gets the default.</summary>
    [JsonPropertyName("default")]
    public string? Default { get; init; }
}

/// <summary>Canonical v1.1 feature flag names (spec §6.2 + IANA §15).</summary>
public static class FeatureFlags
{
    /// <summary>Gets the heartbeat.</summary>
    public const string Heartbeat = "heartbeat";
    /// <summary>Gets the ack.</summary>
    public const string Ack = "ack";
    /// <summary>Gets the list jobs.</summary>
    public const string ListJobs = "list_jobs";
    /// <summary>Gets the subscribe.</summary>
    public const string Subscribe = "subscribe";
    /// <summary>Gets the lease expires at.</summary>
    public const string LeaseExpiresAt = "lease_expires_at";
    /// <summary>Gets the cost budget.</summary>
    public const string CostBudget = "cost.budget";
    /// <summary>Gets the progress.</summary>
    public const string Progress = "progress";
    /// <summary>Gets the result chunk.</summary>
    public const string ResultChunk = "result_chunk";
    /// <summary>Gets the agent versions.</summary>
    public const string AgentVersions = "agent_versions";
    /// <summary>Gets the model use.</summary>
    public const string ModelUse = "model.use";
    /// <summary>Gets the provisioned credentials.</summary>
    public const string ProvisionedCredentials = "provisioned_credentials";

    /// <summary>Gets the all.</summary>
    public static readonly FrozenSet<string> All = new HashSet<string>
    {
        Heartbeat, Ack, ListJobs, Subscribe, LeaseExpiresAt, CostBudget,
        Progress, ResultChunk, AgentVersions, ModelUse, ProvisionedCredentials,
    }.ToFrozenSet();
}

/// <summary>Feature-set intersection helpers (spec §6.2).</summary>
public static class FeatureSet
{
    /// <summary>Returns the intersection of two feature lists. Null operands yield an empty set.</summary>
    public static IReadOnlyList<string> Intersect(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (a is null || a.Count == 0 || b is null || b.Count == 0) return Array.Empty<string>();
        var setB = new HashSet<string>(b, StringComparer.Ordinal);
        return a.Where(setB.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Has.</summary>
    public static bool Has(IReadOnlyList<string>? features, string flag) =>
        features is not null && features.Contains(flag, StringComparer.Ordinal);

    /// <summary>A capability advertisement that turns every v1.1 feature on.</summary>
    public static IReadOnlyList<string> AllFeatures => FeatureFlags.All.ToArray();
}
