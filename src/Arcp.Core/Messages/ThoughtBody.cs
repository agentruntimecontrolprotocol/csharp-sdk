// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the thought body.</summary>
public sealed record ThoughtBody
{
    /// <summary>Gets the text.</summary>
    [JsonPropertyName("text")] public required string Text { get; init; }
}
