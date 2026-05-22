// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>A short-lived, lease-bound credential issued for a job (spec §9.8.1).</summary>
public sealed record ProvisionedCredential
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("scheme")]
    public string Scheme { get; init; } = "bearer";

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("constraints")]
    public CredentialConstraints? Constraints { get; init; }
}
