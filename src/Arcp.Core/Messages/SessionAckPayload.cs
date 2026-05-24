// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session ack payload.</summary>
public sealed record SessionAckPayload
{
    /// <summary>Gets the last processed seq.</summary>
    [JsonPropertyName("last_processed_seq")] public required long LastProcessedSeq { get; init; }
}
