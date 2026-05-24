// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the tool result body.</summary>
public sealed record ToolResultBody
{
    /// <summary>Gets the call id.</summary>
    [JsonPropertyName("call_id")] public required string CallId { get; init; }

    /// <summary>Gets the result.</summary>
    [JsonPropertyName("result")] public JsonElement? Result { get; init; }

    /// <summary>Gets the error.</summary>
    [JsonPropertyName("error")] public ToolError? Error { get; init; }
}
