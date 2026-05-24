// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the result chunk body.</summary>
public sealed record ResultChunkBody
{
    /// <summary>Gets the result id.</summary>
    [JsonPropertyName("result_id")] public required string ResultId { get; init; }

    /// <summary>Gets the chunk seq.</summary>
    [JsonPropertyName("chunk_seq")] public required long ChunkSeq { get; init; }

    /// <summary>Gets the data.</summary>
    [JsonPropertyName("data")] public required string Data { get; init; }

    /// <summary>Gets the encoding.</summary>
    [JsonPropertyName("encoding")] public required string Encoding { get; init; }

    /// <summary>Gets the more.</summary>
    [JsonPropertyName("more")] public required bool More { get; init; }
}
