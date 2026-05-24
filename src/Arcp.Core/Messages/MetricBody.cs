// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the metric body.</summary>
public sealed record MetricBody
{
    /// <summary>Gets the name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>Gets the value.</summary>
    [JsonPropertyName("value")] public required double Value { get; init; }

    /// <summary>Gets the unit.</summary>
    [JsonPropertyName("unit")] public string? Unit { get; init; }

    /// <summary>Gets the dimensions.</summary>
    [JsonPropertyName("dimensions")] public IReadOnlyDictionary<string, string>? Dimensions { get; init; }
}
