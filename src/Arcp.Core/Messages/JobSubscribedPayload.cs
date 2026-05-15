// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobSubscribedPayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("current_status")] public required string CurrentStatus { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    [JsonPropertyName("subscribed_from")] public long? SubscribedFrom { get; init; }

    [JsonPropertyName("replayed")] public bool Replayed { get; init; }
}
