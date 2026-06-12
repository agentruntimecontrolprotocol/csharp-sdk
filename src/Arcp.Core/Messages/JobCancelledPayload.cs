// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>Acknowledgement the runtime sends to the submitting session when a <c>job.cancel</c> is
/// accepted (spec §7.4). The terminal <c>job.error{CANCELLED, final_status:"cancelled"}</c> follows
/// once the run-loop unwinds the agent.</summary>
public sealed record JobCancelledPayload
{
    /// <summary>Gets the job id.</summary>
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    /// <summary>Gets the reason.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}
