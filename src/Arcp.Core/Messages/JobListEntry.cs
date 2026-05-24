// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job list entry.</summary>
public sealed record JobListEntry
{
    /// <summary>Gets the job id.</summary>
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    /// <summary>Gets the agent.</summary>
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    /// <summary>Gets the status.</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }

    /// <summary>Gets the lease.</summary>
    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    /// <summary>Gets the parent job id.</summary>
    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    /// <summary>Gets the created at.</summary>
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the trace id.</summary>
    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    /// <summary>Gets the last event seq.</summary>
    [JsonPropertyName("last_event_seq")] public long? LastEventSeq { get; init; }
}
