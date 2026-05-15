// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;

namespace Arcp.Core.Tracing;

/// <summary>Canonical OpenTelemetry attribute keys for ARCP spans (spec §11).</summary>
public static class TraceAttributes
{
    public const string Direction = "arcp.direction";
    public const string Type = "arcp.type";
    public const string Id = "arcp.id";
    public const string SessionId = "arcp.session_id";
    public const string JobId = "arcp.job_id";
    public const string TraceId = "arcp.trace_id";
    public const string EventSeq = "arcp.event_seq";
    public const string Agent = "arcp.agent";
    public const string LeaseCapabilities = "arcp.lease.capabilities";
    public const string LeaseExpiresAt = "arcp.lease.expires_at";
    public const string BudgetRemaining = "arcp.budget.remaining";
}

/// <summary>The shared ARCP <see cref="ActivitySource"/>s. Consumers register them in their tracer
/// provider via <c>AddSource("Arcp.Transport")</c> / <c>AddSource("Arcp.Runtime")</c>.</summary>
public static class ArcpDiagnostics
{
    public const string TransportSourceName = "Arcp.Transport";
    public const string RuntimeSourceName = "Arcp.Runtime";

    public static ActivitySource Transport { get; } = new(TransportSourceName, "1.1.0");

    public static ActivitySource Runtime { get; } = new(RuntimeSourceName, "1.1.0");
}
