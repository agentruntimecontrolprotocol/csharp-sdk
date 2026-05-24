// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the auth credential.</summary>
public sealed record AuthCredential
{
    /// <summary>Gets the scheme.</summary>
    [JsonPropertyName("scheme")] public string Scheme { get; init; } = "bearer";

    /// <summary>Gets the token.</summary>
    [JsonPropertyName("token")] public string? Token { get; init; }
}
