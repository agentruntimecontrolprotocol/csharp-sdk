// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the session bye payload.</summary>
public sealed record SessionByePayload
{
    /// <summary>Gets the reason.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}
