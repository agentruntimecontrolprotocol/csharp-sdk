// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the tool call body.</summary>
public sealed record ToolCallBody
{
    /// <summary>Gets the tool.</summary>
    [JsonPropertyName("tool")] public required string Tool { get; init; }

    /// <summary>Gets the call id.</summary>
    [JsonPropertyName("call_id")] public required string CallId { get; init; }

    /// <summary>Gets the args.</summary>
    [JsonPropertyName("args")] public JsonElement? Args { get; init; }
}
