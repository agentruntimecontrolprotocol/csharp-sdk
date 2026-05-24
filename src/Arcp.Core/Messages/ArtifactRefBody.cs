// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Gets the artifact ref body.</summary>
public sealed record ArtifactRefBody
{
    /// <summary>Gets the uri.</summary>
    [JsonPropertyName("uri")] public required string Uri { get; init; }

    /// <summary>Gets the content type.</summary>
    [JsonPropertyName("content_type")] public string? ContentType { get; init; }

    /// <summary>Gets the byte size.</summary>
    [JsonPropertyName("byte_size")] public long? ByteSize { get; init; }

    /// <summary>Gets the sha256.</summary>
    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
}
