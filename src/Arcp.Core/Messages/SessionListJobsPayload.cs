// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session list jobs payload.</summary>
public sealed record SessionListJobsPayload
{
    /// <summary>Gets the filter.</summary>
    [JsonPropertyName("filter")] public JobListFilter? Filter { get; init; }

    /// <summary>Gets the limit.</summary>
    [JsonPropertyName("limit")] public int? Limit { get; init; }

    /// <summary>Gets the cursor.</summary>
    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
}
