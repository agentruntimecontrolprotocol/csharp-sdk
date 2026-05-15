// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record DelegateBody
{
    [JsonPropertyName("child_job_id")] public required string ChildJobId { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("input")] public JsonElement? Input { get; init; }
}
