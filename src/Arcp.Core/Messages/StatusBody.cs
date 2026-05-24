// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the status body.</summary>
public sealed record StatusBody
{
    /// <summary>Gets the phase.</summary>
    [JsonPropertyName("phase")] public required string Phase { get; init; }

    /// <summary>Gets the message.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    /// <summary>Gets the credential id.</summary>
    [JsonPropertyName("credential_id")] public string? CredentialId { get; init; }

    /// <summary>Gets the credential value.</summary>
    [JsonPropertyName("credential_value")] public string? CredentialValue { get; init; }
}
