// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job list filter.</summary>
public sealed record JobListFilter
{
    /// <summary>Gets the status.</summary>
    [JsonPropertyName("status")] public IReadOnlyList<string>? Status { get; init; }

    /// <summary>Gets the agent.</summary>
    [JsonPropertyName("agent")] public string? Agent { get; init; }

    /// <summary>Gets the created after.</summary>
    [JsonPropertyName("created_after")] public DateTimeOffset? CreatedAfter { get; init; }
}
