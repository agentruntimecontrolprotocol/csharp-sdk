// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobSubmitPayload
{
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("input")] public JsonElement? Input { get; init; }

    [JsonPropertyName("lease_request")] public Lease? LeaseRequest { get; init; }

    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    [JsonPropertyName("idempotency_key")] public string? IdempotencyKey { get; init; }

    [JsonPropertyName("max_runtime_sec")] public int? MaxRuntimeSec { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }
}
