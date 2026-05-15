// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobEventPayload
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("ts")] public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("body")] public JsonElement Body { get; init; }
}
