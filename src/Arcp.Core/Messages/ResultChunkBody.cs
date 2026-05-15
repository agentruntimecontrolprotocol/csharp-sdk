// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record ResultChunkBody
{
    [JsonPropertyName("result_id")] public required string ResultId { get; init; }

    [JsonPropertyName("chunk_seq")] public required long ChunkSeq { get; init; }

    [JsonPropertyName("data")] public required string Data { get; init; }

    [JsonPropertyName("encoding")] public required string Encoding { get; init; }

    [JsonPropertyName("more")] public required bool More { get; init; }
}
