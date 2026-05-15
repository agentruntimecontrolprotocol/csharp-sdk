// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobListEntry
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    [JsonPropertyName("last_event_seq")] public long? LastEventSeq { get; init; }
}
