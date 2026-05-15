// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record ProgressBody
{
    [JsonPropertyName("current")] public required long Current { get; init; }

    [JsonPropertyName("total")] public long? Total { get; init; }

    [JsonPropertyName("units")] public string? Units { get; init; }

    [JsonPropertyName("message")] public string? Message { get; init; }

    public ProgressBody Validate()
    {
        if (Current < 0)
            throw new Errors.InvalidRequestException("progress.current MUST be ≥ 0 (spec §8.2.1)");
        if (Total is { } t && Current > t)
            throw new Errors.InvalidRequestException("progress.current SHOULD be ≤ total (spec §8.2.1)");
        return this;
    }
}
