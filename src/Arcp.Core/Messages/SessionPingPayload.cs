// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session ping payload.</summary>
public sealed record SessionPingPayload
{
    /// <summary>Gets the nonce.</summary>
    [JsonPropertyName("nonce")] public required string Nonce { get; init; }

    /// <summary>Gets the sent at.</summary>
    [JsonPropertyName("sent_at")] public required DateTimeOffset SentAt { get; init; }
}
