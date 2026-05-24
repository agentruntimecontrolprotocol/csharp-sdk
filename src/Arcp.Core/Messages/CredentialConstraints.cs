// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>Constraints baked into a provisioned credential (spec §9.8.1).</summary>
public sealed record CredentialConstraints
{
    /// <summary>Gets the cost budget.</summary>
    [JsonPropertyName("cost.budget")]
    public IReadOnlyList<string>? CostBudget { get; init; }

    /// <summary>Gets the model use.</summary>
    [JsonPropertyName("model.use")]
    public IReadOnlyList<string>? ModelUse { get; init; }

    /// <summary>Gets the expires at.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }
}
