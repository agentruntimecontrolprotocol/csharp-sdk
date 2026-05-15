// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record SessionListJobsPayload
{
    [JsonPropertyName("filter")] public JobListFilter? Filter { get; init; }

    [JsonPropertyName("limit")] public int? Limit { get; init; }

    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
}
