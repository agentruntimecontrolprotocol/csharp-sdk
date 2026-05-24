// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session hello payload.</summary>
public sealed record SessionHelloPayload
{
    /// <summary>Gets the client.</summary>
    [JsonPropertyName("client")] public required ClientInfo Client { get; init; }

    /// <summary>Gets the auth.</summary>
    [JsonPropertyName("auth")] public AuthCredential? Auth { get; init; }

    /// <summary>Gets the capabilities.</summary>
    [JsonPropertyName("capabilities")] public required Capabilities Capabilities { get; init; }

    /// <summary>If present, the runtime treats this as a resume attempt (spec §6.3).</summary>
    [JsonPropertyName("resume_token")] public string? ResumeToken { get; init; }

    /// <summary>Gets the last event seq.</summary>
    [JsonPropertyName("last_event_seq")] public long? LastEventSeq { get; init; }
}
