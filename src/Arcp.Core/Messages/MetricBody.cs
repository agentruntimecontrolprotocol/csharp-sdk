// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record MetricBody
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("value")] public required double Value { get; init; }

    [JsonPropertyName("unit")] public string? Unit { get; init; }

    [JsonPropertyName("dimensions")] public IReadOnlyDictionary<string, string>? Dimensions { get; init; }
}
