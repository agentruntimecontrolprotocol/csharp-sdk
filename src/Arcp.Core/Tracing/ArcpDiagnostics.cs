// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;

namespace Arcp.Core.Tracing;

/// <summary>Canonical OpenTelemetry attribute keys for ARCP spans (spec §11).</summary>
public static class TraceAttributes
{
    /// <summary>Gets the direction.</summary>
    public const string Direction = "arcp.direction";
    /// <summary>Gets the type.</summary>
    public const string Type = "arcp.type";
    /// <summary>Gets the id.</summary>
    public const string Id = "arcp.id";
    /// <summary>Gets the session id.</summary>
    public const string SessionId = "arcp.session_id";
    /// <summary>Gets the job id.</summary>
    public const string JobId = "arcp.job_id";
    /// <summary>Gets the trace id.</summary>
    public const string TraceId = "arcp.trace_id";
    /// <summary>Gets the event seq.</summary>
    public const string EventSeq = "arcp.event_seq";
    /// <summary>Gets the agent.</summary>
    public const string Agent = "arcp.agent";
    /// <summary>Gets the lease capabilities.</summary>
    public const string LeaseCapabilities = "arcp.lease.capabilities";
    /// <summary>Gets the lease expires at.</summary>
    public const string LeaseExpiresAt = "arcp.lease.expires_at";
    /// <summary>Gets the budget remaining.</summary>
    public const string BudgetRemaining = "arcp.budget.remaining";
}

/// <summary>The shared ARCP <see cref="ActivitySource"/>s. Consumers register them in their tracer
/// provider via <c>AddSource("Arcp.Transport")</c> / <c>AddSource("Arcp.Runtime")</c>.</summary>
public static class ArcpDiagnostics
{
    /// <summary>Gets the transport source name.</summary>
    public const string TransportSourceName = "Arcp.Transport";
    /// <summary>Gets the runtime source name.</summary>
    public const string RuntimeSourceName = "Arcp.Runtime";

    /// <summary>Gets the transport.</summary>
    public static ActivitySource Transport { get; } = new(TransportSourceName, "1.1.0");

    /// <summary>Gets the runtime.</summary>
    public static ActivitySource Runtime { get; } = new(RuntimeSourceName, "1.1.0");
}
