// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record ToolError
{
    [JsonPropertyName("code")] public required string Code { get; init; }

    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("retryable")] public bool Retryable { get; init; }
}
