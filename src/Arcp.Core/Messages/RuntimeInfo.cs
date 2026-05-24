// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the runtime info.</summary>
public sealed record RuntimeInfo
{
    /// <summary>Gets the name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>Gets the version.</summary>
    [JsonPropertyName("version")] public required string Version { get; init; }
}
