// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the progress body.</summary>
public sealed record ProgressBody
{
    /// <summary>Gets the current.</summary>
    [JsonPropertyName("current")] public required long Current { get; init; }

    /// <summary>Gets the total.</summary>
    [JsonPropertyName("total")] public long? Total { get; init; }

    /// <summary>Gets the units.</summary>
    [JsonPropertyName("units")] public string? Units { get; init; }

    /// <summary>Gets the message.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    /// <summary>Validate.</summary>
    public ProgressBody Validate()
    {
        if (Current < 0)
            throw new Errors.InvalidRequestException("progress.current MUST be ≥ 0 (spec §8.2.1)");
        if (Total is { } t && Current > t)
            throw new Errors.InvalidRequestException("progress.current SHOULD be ≤ total (spec §8.2.1)");
        return this;
    }
}
