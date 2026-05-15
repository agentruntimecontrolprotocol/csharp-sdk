// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobResultPayload
{
    [JsonPropertyName("final_status")] public string FinalStatus { get; init; } = "success";

    [JsonPropertyName("result")] public JsonElement? Result { get; init; }

    [JsonPropertyName("result_id")] public string? ResultId { get; init; }

    [JsonPropertyName("result_size")] public long? ResultSize { get; init; }

    [JsonPropertyName("summary")] public string? Summary { get; init; }
}
