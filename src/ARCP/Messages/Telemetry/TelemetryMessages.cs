using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using ARCP.Envelope;
using ARCP.Ids;

namespace ARCP.Messages.Telemetry;

/// <summary>§17.2 log levels.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
public enum LogLevel
{
    /// <summary>Trace.</summary>
    [JsonStringEnumMemberName("trace")]
    Trace,

    /// <summary>Debug.</summary>
    [JsonStringEnumMemberName("debug")]
    Debug,

    /// <summary>Info.</summary>
    [JsonStringEnumMemberName("info")]
    Info,

    /// <summary>Warn.</summary>
    [JsonStringEnumMemberName("warn")]
    Warn,

    /// <summary>Error.</summary>
    [JsonStringEnumMemberName("error")]
    Error,

    /// <summary>Critical.</summary>
    [JsonStringEnumMemberName("critical")]
    Critical,
}

/// <summary>§6.2 generic event.</summary>
public sealed record EventEmit(
    string Name,
    IReadOnlyDictionary<string, JsonElement>? Attrs = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "event.emit";
}

/// <summary>§17.2 structured log event.</summary>
public sealed record LogMessage(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, JsonElement>? Attributes = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "log";
}

/// <summary>§17.3 metric sample.</summary>
public sealed record Metric(
    string Name,
    double Value,
    string Unit,
    IReadOnlyDictionary<string, string>? Dims = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "metric";
}

/// <summary>§17.1 trace span event.</summary>
public sealed record TraceSpan(
    Ids.TraceId TraceId,
    Ids.SpanId SpanId,
    string Name,
    DateTimeOffset StartTime,
    Ids.SpanId? ParentSpanId = null,
    double? DurationMs = null,
    string? Status = null,
    IReadOnlyDictionary<string, JsonElement>? Attributes = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "trace.span";
}

/// <summary>
/// Reserved metric names per §17.3.1. Runtimes producing these concepts
/// <strong>MUST</strong> use these names with the indicated units;
/// non-standard variants <strong>MUST</strong> be namespaced.
/// </summary>
public static class ReservedMetrics
{
    /// <summary>Token usage; <c>dims.kind</c> ∈ <c>input</c>, <c>output</c>, <c>cache_read</c>, <c>cache_write</c>.</summary>
    public const string TokensUsed = "tokens.used";

    /// <summary>Cost in USD with up to 6 fractional digits.</summary>
    public const string CostUsd = "cost.usd";

    /// <summary>Wall-clock GPU time, summed across devices.</summary>
    public const string GpuSeconds = "gpu.seconds";

    /// <summary>One per <c>tool.invoke</c>.</summary>
    public const string ToolInvocations = "tool.invocations";

    /// <summary>Latency in milliseconds; <c>dims.phase</c> ∈ <c>queue</c>, <c>exec</c>, <c>total</c>.</summary>
    public const string LatencyMs = "latency.ms";

    /// <summary>Inbound bytes at runtime boundary.</summary>
    public const string BytesIn = "bytes.in";

    /// <summary>Outbound bytes at runtime boundary.</summary>
    public const string BytesOut = "bytes.out";

    /// <summary>Total errors; <c>dims.code</c> carries the canonical error code.</summary>
    public const string ErrorsTotal = "errors.total";

    /// <summary>Reserved metric names mapped to their canonical units.</summary>
    public static FrozenDictionary<string, string> ExpectedUnits { get; } = new Dictionary<string, string>
    {
        [TokensUsed] = "tokens",
        [CostUsd] = "usd",
        [GpuSeconds] = "seconds",
        [ToolInvocations] = "count",
        [LatencyMs] = "ms",
        [BytesIn] = "bytes",
        [BytesOut] = "bytes",
        [ErrorsTotal] = "count",
    }.ToFrozenDictionary();

    /// <summary>Whether <paramref name="name" /> is one of the reserved metric names.</summary>
    /// <param name="name">The metric name.</param>
    /// <returns><see langword="true" /> if reserved.</returns>
    public static bool IsReserved(string name) =>
        !string.IsNullOrEmpty(name) && ExpectedUnits.ContainsKey(name);
}
