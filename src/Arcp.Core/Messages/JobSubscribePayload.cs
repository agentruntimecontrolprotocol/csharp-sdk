// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job subscribe payload.</summary>
public sealed record JobSubscribePayload
{
    /// <summary>Gets the job id.</summary>
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    /// <summary>Gets the from event seq.</summary>
    [JsonPropertyName("from_event_seq")] public long? FromEventSeq { get; init; }

    /// <summary>Gets the history.</summary>
    [JsonPropertyName("history")] public bool History { get; init; }
}
