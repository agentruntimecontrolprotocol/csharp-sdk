// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>A short-lived, lease-bound credential issued for a job (spec §9.8.1).</summary>
public sealed record ProvisionedCredential
{
    /// <summary>Gets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the scheme.</summary>
    [JsonPropertyName("scheme")]
    public string Scheme { get; init; } = "bearer";

    /// <summary>Gets the value.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Gets the endpoint.</summary>
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    /// <summary>Gets the profile.</summary>
    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    /// <summary>Gets the constraints.</summary>
    [JsonPropertyName("constraints")]
    public CredentialConstraints? Constraints { get; init; }
}
