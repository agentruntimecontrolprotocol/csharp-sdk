// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the delegate body.</summary>
public sealed record DelegateBody
{
    /// <summary>Gets the child job id.</summary>
    [JsonPropertyName("child_job_id")] public required string ChildJobId { get; init; }

    /// <summary>Gets the agent.</summary>
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    /// <summary>Gets the input.</summary>
    [JsonPropertyName("input")] public JsonElement? Input { get; init; }
}
