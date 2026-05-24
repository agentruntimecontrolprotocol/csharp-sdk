// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job submit payload.</summary>
public sealed record JobSubmitPayload
{
    /// <summary>Gets the agent.</summary>
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    /// <summary>Gets the input.</summary>
    [JsonPropertyName("input")] public JsonElement? Input { get; init; }

    /// <summary>Gets the lease request.</summary>
    [JsonPropertyName("lease_request")] public Lease? LeaseRequest { get; init; }

    /// <summary>Gets the lease constraints.</summary>
    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    /// <summary>Gets the idempotency key.</summary>
    [JsonPropertyName("idempotency_key")] public string? IdempotencyKey { get; init; }

    /// <summary>Gets the max runtime sec.</summary>
    [JsonPropertyName("max_runtime_sec")] public int? MaxRuntimeSec { get; init; }

    /// <summary>Gets the parent job id.</summary>
    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }
}
