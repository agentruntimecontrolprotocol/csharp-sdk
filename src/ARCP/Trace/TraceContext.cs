using ARCP.Ids;

namespace ARCP.Trace;

/// <summary>
/// Per-message trace context derived from envelope headers per RFC-0001-v2
/// §17.1. Flowed across asynchronous boundaries by <see cref="Tracing" />.
/// </summary>
/// <param name="TraceId">Stable id for one user-visible request or workflow.</param>
/// <param name="SpanId">Span id for the current operation.</param>
/// <param name="ParentSpanId">Parent span id when this message is part of a trace tree.</param>
public readonly record struct TraceContext(
    TraceId TraceId,
    SpanId SpanId,
    SpanId? ParentSpanId = null)
{
    /// <summary>
    /// Create a fresh root trace context.
    /// </summary>
    /// <returns>A trace context with new trace and span ids and no parent.</returns>
    public static TraceContext NewRoot() => new(
        TraceId: TraceId.New(),
        SpanId: SpanId.New(),
        ParentSpanId: null);

    /// <summary>
    /// Create a child trace context that inherits <see cref="TraceId" /> from
    /// this context and points back to <see cref="SpanId" /> as parent.
    /// </summary>
    /// <returns>A child trace context.</returns>
    public TraceContext NewChild() => new(
        TraceId: TraceId,
        SpanId: SpanId.New(),
        ParentSpanId: SpanId);
}
