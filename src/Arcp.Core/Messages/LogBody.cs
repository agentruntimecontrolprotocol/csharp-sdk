// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the log body.</summary>
public sealed record LogBody
{
    /// <summary>Gets the level.</summary>
    [JsonPropertyName("level")] public required string Level { get; init; }

    /// <summary>Gets the message.</summary>
    [JsonPropertyName("message")] public required string Message { get; init; }
}
