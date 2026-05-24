// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job result payload.</summary>
public sealed record JobResultPayload
{
    /// <summary>Gets the final status.</summary>
    [JsonPropertyName("final_status")] public string FinalStatus { get; init; } = "success";

    /// <summary>Gets the result.</summary>
    [JsonPropertyName("result")] public JsonElement? Result { get; init; }

    /// <summary>Gets the result id.</summary>
    [JsonPropertyName("result_id")] public string? ResultId { get; init; }

    /// <summary>Gets the result size.</summary>
    [JsonPropertyName("result_size")] public long? ResultSize { get; init; }

    /// <summary>Gets the summary.</summary>
    [JsonPropertyName("summary")] public string? Summary { get; init; }
}
