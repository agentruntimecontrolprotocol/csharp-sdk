// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session jobs payload.</summary>
public sealed record SessionJobsPayload
{
    /// <summary>Gets the request id.</summary>
    [JsonPropertyName("request_id")] public string? RequestId { get; init; }

    /// <summary>Gets the jobs.</summary>
    [JsonPropertyName("jobs")] public required IReadOnlyList<JobListEntry> Jobs { get; init; }

    /// <summary>Gets the next cursor.</summary>
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; init; }
}
