// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the job cancel payload.</summary>
public sealed record JobCancelPayload
{
    /// <summary>Gets the job id.</summary>
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    /// <summary>Gets the reason.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}
