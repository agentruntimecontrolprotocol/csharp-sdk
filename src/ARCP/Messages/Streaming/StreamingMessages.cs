using System.Text.Json;
using System.Text.Json.Serialization;
using ARCP.Envelope;
using ARCP.Errors;

namespace ARCP.Messages.Streaming;

/// <summary>§11.1 stream kinds. Implementations SHOULD treat unknown kinds as <see cref="Event" />.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StreamKind>))]
public enum StreamKind
{
    /// <summary>Plain text output.</summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>Opaque bytes (§11.3 — base64 in-envelope only in v0.1).</summary>
    [JsonStringEnumMemberName("binary")]
    Binary,

    /// <summary>Structured JSON events.</summary>
    [JsonStringEnumMemberName("event")]
    Event,

    /// <summary>Structured log lines.</summary>
    [JsonStringEnumMemberName("log")]
    Log,

    /// <summary>Telemetry samples.</summary>
    [JsonStringEnumMemberName("metric")]
    Metric,

    /// <summary>Model reasoning / chain-of-thought (§11.4).</summary>
    [JsonStringEnumMemberName("thought")]
    Thought,
}

/// <summary>§11 open a new stream.</summary>
public sealed record StreamOpen(
    StreamKind Kind,
    string? ContentType = null,
    string? Encoding = null,
    Ids.JobId? RelatedJobId = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "stream.open";
}

/// <summary>§11 stream chunk. Per-kind fields populated as appropriate.</summary>
public sealed record StreamChunk : MessageType
{
    /// <summary>Per-stream sequence number, monotonic from 0.</summary>
    public required long Sequence { get; init; }

    /// <summary>For <see cref="StreamKind.Text" /> / <see cref="StreamKind.Binary" />: payload data (base64 for binary).</summary>
    public string? Data { get; init; }

    /// <summary>For <see cref="StreamKind.Event" />: structured event.</summary>
    public JsonElement? Event { get; init; }

    /// <summary>For <see cref="StreamKind.Log" />: log entry.</summary>
    public JsonElement? Log { get; init; }

    /// <summary>For <see cref="StreamKind.Metric" />: metric sample.</summary>
    public JsonElement? Metric { get; init; }

    /// <summary>For <see cref="StreamKind.Thought" /> (§11.4): role.</summary>
    public string? Role { get; init; }

    /// <summary>For <see cref="StreamKind.Thought" />: free-form content.</summary>
    public string? Content { get; init; }

    /// <summary>For <see cref="StreamKind.Thought" />: redaction flag.</summary>
    public bool? Redacted { get; init; }

    /// <summary>Optional content-type override.</summary>
    public string? ContentType { get; init; }

    /// <summary>Optional SHA-256 checksum (binary streams).</summary>
    public string? Sha256 { get; init; }

    /// <inheritdoc />
    public override string WireType => "stream.chunk";
}

/// <summary>§11 close a stream cleanly.</summary>
public sealed record StreamClose(
    string? Reason = null,
    long? TotalChunks = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "stream.close";
}

/// <summary>§11 / §18 stream terminated with an error.</summary>
public sealed record StreamError(
    ErrorCode Code,
    string Message,
    bool? Retryable = null,
    IReadOnlyDictionary<string, JsonElement>? Details = null,
    string? TraceId = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "stream.error";
}
