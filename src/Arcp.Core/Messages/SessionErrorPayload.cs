// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session error payload.</summary>
public sealed record SessionErrorPayload
{
    /// <summary>Gets the code.</summary>
    [JsonPropertyName("code")] public required string Code { get; init; }

    /// <summary>Gets the message.</summary>
    [JsonPropertyName("message")] public required string Message { get; init; }

    /// <summary>Gets the retryable.</summary>
    [JsonPropertyName("retryable")] public bool Retryable { get; init; }

    /// <summary>Gets the detail.</summary>
    [JsonPropertyName("detail")] public string? Detail { get; init; }
}
