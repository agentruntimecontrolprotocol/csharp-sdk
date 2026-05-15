// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record ToolResultBody
{
    [JsonPropertyName("call_id")] public required string CallId { get; init; }

    [JsonPropertyName("result")] public JsonElement? Result { get; init; }

    [JsonPropertyName("error")] public ToolError? Error { get; init; }
}
