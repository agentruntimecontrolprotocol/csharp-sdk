// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobListFilter
{
    [JsonPropertyName("status")] public IReadOnlyList<string>? Status { get; init; }

    [JsonPropertyName("agent")] public string? Agent { get; init; }

    [JsonPropertyName("created_after")] public DateTimeOffset? CreatedAfter { get; init; }
}
