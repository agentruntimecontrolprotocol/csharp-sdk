// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job subscribed payload.</summary>
public sealed record JobSubscribedPayload
{
    /// <summary>Gets the job id.</summary>
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    /// <summary>Gets the current status.</summary>
    [JsonPropertyName("current_status")] public required string CurrentStatus { get; init; }

    /// <summary>Gets the agent.</summary>
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    /// <summary>Gets the lease.</summary>
    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    /// <summary>Gets the lease constraints.</summary>
    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    /// <summary>Gets the parent job id.</summary>
    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    /// <summary>Gets the trace id.</summary>
    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    /// <summary>Gets the subscribed from.</summary>
    [JsonPropertyName("subscribed_from")] public long? SubscribedFrom { get; init; }

    /// <summary>Gets the replayed.</summary>
    [JsonPropertyName("replayed")] public bool Replayed { get; init; }

    /// <summary>Gets the credentials.</summary>
    [JsonPropertyName("credentials")] public IReadOnlyList<ProvisionedCredential>? Credentials { get; init; }
}
