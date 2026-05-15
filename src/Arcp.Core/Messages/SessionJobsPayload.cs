// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record SessionJobsPayload
{
    [JsonPropertyName("request_id")] public string? RequestId { get; init; }

    [JsonPropertyName("jobs")] public required IReadOnlyList<JobListEntry> Jobs { get; init; }

    [JsonPropertyName("next_cursor")] public string? NextCursor { get; init; }
}
