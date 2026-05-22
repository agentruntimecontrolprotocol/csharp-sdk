// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>Constraints baked into a provisioned credential (spec §9.8.1).</summary>
public sealed record CredentialConstraints
{
    [JsonPropertyName("cost.budget")]
    public IReadOnlyList<string>? CostBudget { get; init; }

    [JsonPropertyName("model.use")]
    public IReadOnlyList<string>? ModelUse { get; init; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }
}
