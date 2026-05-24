// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job accepted payload.</summary>
public sealed record JobAcceptedPayload
{
    /// <summary>Gets the job id.</summary>
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    /// <summary>Gets the agent.</summary>
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    /// <summary>Gets the lease.</summary>
    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    /// <summary>Gets the lease constraints.</summary>
    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    /// <summary>Gets the budget.</summary>
    [JsonPropertyName("budget")] public IReadOnlyDictionary<string, decimal>? Budget { get; init; }

    /// <summary>Gets the accepted at.</summary>
    [JsonPropertyName("accepted_at")] public required DateTimeOffset AcceptedAt { get; init; }

    /// <summary>Gets the trace id.</summary>
    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    /// <summary>Gets the parent job id.</summary>
    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    /// <summary>Gets the credentials.</summary>
    [JsonPropertyName("credentials")] public IReadOnlyList<ProvisionedCredential>? Credentials { get; init; }
}
