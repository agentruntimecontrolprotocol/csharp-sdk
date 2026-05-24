// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job event payload.</summary>
public sealed record JobEventPayload
{
    /// <summary>Gets the kind.</summary>
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    /// <summary>Gets the ts.</summary>
    [JsonPropertyName("ts")] public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the body.</summary>
    [JsonPropertyName("body")] public JsonElement Body { get; init; }
}
