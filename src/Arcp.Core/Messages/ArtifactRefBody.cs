// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

public sealed record ArtifactRefBody
{
    [JsonPropertyName("uri")] public required string Uri { get; init; }

    [JsonPropertyName("content_type")] public string? ContentType { get; init; }

    [JsonPropertyName("byte_size")] public long? ByteSize { get; init; }

    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
}
