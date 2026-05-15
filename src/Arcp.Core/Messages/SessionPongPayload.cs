// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record SessionPongPayload
{
    [JsonPropertyName("ping_nonce")] public required string PingNonce { get; init; }

    [JsonPropertyName("received_at")] public required DateTimeOffset ReceivedAt { get; init; }
}
