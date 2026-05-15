// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcp.Core.Wire;

/// <summary>The ARCP wire envelope (spec §5.1). Carries the message type discriminator, identifiers,
/// and a payload object whose shape depends on <see cref="Type"/>.</summary>
public sealed record Envelope
{
    /// <summary>Protocol version. Always <c>"1"</c> in ARCP v1.x.</summary>
    [JsonPropertyName("arcp")]
    public string Arcp { get; init; } = "1";

    /// <summary>Unique message identifier (ULID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "msg_" + Ulid.NewUlid();

    /// <summary>Message type discriminator (e.g. <c>session.hello</c>, <c>job.event</c>).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("span_id")]
    public string? SpanId { get; init; }

    [JsonPropertyName("parent_span_id")]
    public string? ParentSpanId { get; init; }

    [JsonPropertyName("job_id")]
    public string? JobId { get; init; }

    /// <summary>Session-scoped, monotonic sequence number for <c>job.event</c>, <c>job.result</c>,
    /// and <c>job.error</c> envelopes (spec §8.3). <see langword="null"/> for control messages.</summary>
    [JsonPropertyName("event_seq")]
    public long? EventSeq { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Type-specific payload object. The custom envelope JSON converter dispatches on
    /// <see cref="Type"/>.</summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    /// <summary>Unknown top-level envelope fields preserved verbatim per spec §5.1 ("MUST ignore
    /// unknown fields") so they round-trip without loss.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extensions { get; init; }
}
