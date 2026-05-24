// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session welcome payload.</summary>
public sealed record SessionWelcomePayload
{
    /// <summary>Gets the runtime.</summary>
    [JsonPropertyName("runtime")] public required RuntimeInfo Runtime { get; init; }

    /// <summary>Gets the resume token.</summary>
    [JsonPropertyName("resume_token")] public string? ResumeToken { get; init; }

    /// <summary>Gets the resume window sec.</summary>
    [JsonPropertyName("resume_window_sec")] public int? ResumeWindowSec { get; init; }

    /// <summary>Gets the heartbeat interval sec.</summary>
    [JsonPropertyName("heartbeat_interval_sec")] public int? HeartbeatIntervalSec { get; init; }

    /// <summary>Gets the capabilities.</summary>
    [JsonPropertyName("capabilities")] public required Capabilities Capabilities { get; init; }
}
