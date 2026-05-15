// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record SessionWelcomePayload
{
    [JsonPropertyName("runtime")] public required RuntimeInfo Runtime { get; init; }

    [JsonPropertyName("resume_token")] public string? ResumeToken { get; init; }

    [JsonPropertyName("resume_window_sec")] public int? ResumeWindowSec { get; init; }

    [JsonPropertyName("heartbeat_interval_sec")] public int? HeartbeatIntervalSec { get; init; }

    [JsonPropertyName("capabilities")] public required Capabilities Capabilities { get; init; }
}
