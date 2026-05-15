// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record JobSubscribePayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("from_event_seq")] public long? FromEventSeq { get; init; }

    [JsonPropertyName("history")] public bool History { get; init; }
}
