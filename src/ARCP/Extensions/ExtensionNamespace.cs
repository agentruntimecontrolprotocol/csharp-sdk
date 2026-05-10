using System.Collections.Frozen;
using System.Text.RegularExpressions;
using ARCP.Errors;

namespace ARCP.Extensions;

/// <summary>
/// Helpers for §21.1 extension namespace validation and §21.3 unknown-message
/// classification.
/// </summary>
public static partial class ExtensionNamespace
{
    /// <summary>
    /// Pattern for an extension message type or extension envelope-field key.
    /// Per §21.1 the canonical forms are
    /// <c>arcpx.&lt;vendor&gt;.&lt;name&gt;.v&lt;n&gt;</c> or a reverse-DNS prefix
    /// like <c>com.acme.workflow.v2</c>. The bare <c>x-</c> prefix is reserved
    /// for transport-internal experimental fields and <strong>MUST NOT</strong>
    /// appear in long-lived deployments.
    /// </summary>
    [GeneratedRegex(@"^(?!x-)[a-z][a-z0-9_-]*(?:\.[a-z0-9_-]+){2,}\.v\d+$")]
    private static partial Regex ExtensionNamePattern();

    /// <summary>Whether <paramref name="name" /> is a syntactically valid extension namespace per §21.1.</summary>
    /// <param name="name">The candidate namespace.</param>
    /// <returns><see langword="true" /> if it matches the §21.1 grammar.</returns>
    public static bool IsValid(string name) =>
        !string.IsNullOrEmpty(name) && ExtensionNamePattern().IsMatch(name);

    /// <summary>
    /// Closed set of core message types defined by RFC-0001-v2 §6.2. Anything
    /// not in this set must either be an extension (validated against
    /// <see cref="IsValid" />) or rejected as unknown per §21.3.
    /// </summary>
    public static FrozenSet<string> CoreMessageTypes { get; } = new[]
    {
        // Identity & Authentication
        "session.open", "session.challenge", "session.authenticate",
        "session.accepted", "session.unauthenticated", "session.rejected",
        "session.refresh", "session.evicted", "session.close",
        // Control
        "ping", "pong", "ack", "nack",
        "cancel", "cancel.accepted", "cancel.refused",
        "interrupt", "resume", "backpressure",
        "checkpoint.create", "checkpoint.restore",
        // Execution
        "tool.invoke", "tool.result", "tool.error",
        "job.accepted", "job.started", "job.progress", "job.heartbeat",
        "job.checkpoint", "job.completed", "job.failed", "job.cancelled",
        "job.schedule",
        "workflow.start", "workflow.complete",
        "agent.delegate", "agent.handoff",
        // Streaming
        "stream.open", "stream.chunk", "stream.close", "stream.error",
        // Human-in-the-Loop
        "human.input.request", "human.input.response",
        "human.choice.request", "human.choice.response",
        "human.input.cancelled",
        // Permissions & Leases
        "permission.request", "permission.grant", "permission.deny",
        "lease.granted", "lease.extended", "lease.revoked", "lease.refresh",
        // Subscriptions
        "subscribe", "subscribe.accepted", "subscribe.event",
        "unsubscribe", "subscribe.closed",
        // Artifacts
        "artifact.put", "artifact.fetch", "artifact.ref", "artifact.release",
        // Telemetry
        "event.emit", "log", "metric", "trace.span",
    }.ToFrozenSet();

    private static readonly FrozenSet<string> CorePrefixes = new[]
    {
        "session.", "ping", "pong", "ack", "nack",
        "cancel", "interrupt", "resume", "backpressure",
        "checkpoint.", "tool.", "job.", "workflow.", "agent.",
        "stream.", "human.", "permission.", "lease.",
        "subscribe", "unsubscribe", "artifact.",
        "event.", "log", "metric", "trace.",
    }.ToFrozenSet();

    /// <summary>Whether <paramref name="type" /> is one of the closed set of core types (§6.2).</summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> if it is a core type.</returns>
    public static bool IsCoreType(string type) =>
        !string.IsNullOrEmpty(type) && CoreMessageTypes.Contains(type);

    /// <summary>
    /// Whether <paramref name="type" /> <em>looks like</em> a core type even if
    /// not in the closed set, per §21.3 (e.g. <c>session.something_invalid</c>
    /// matches <c>session.</c> prefix).
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> if it matches a core prefix.</returns>
    public static bool LooksLikeCoreType(string type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return false;
        }

        if (CoreMessageTypes.Contains(type))
        {
            return true;
        }

        foreach (string prefix in CorePrefixes)
        {
            if (prefix.EndsWith('.') && type.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
            if (type.Equals(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validate the keys of an envelope <c>extensions</c> object per §21.1.
    /// The reserved key <c>optional</c> is allowed bare; all others must be
    /// valid extension namespaces.
    /// </summary>
    /// <param name="extensions">The extensions object.</param>
    /// <exception cref="InvalidArgumentException">
    /// If any non-reserved key fails <see cref="IsValid" />.
    /// </exception>
    public static void ValidateExtensionsObject(IReadOnlyDictionary<string, object?> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (string key in extensions.Keys)
        {
            if (key == "optional")
            {
                continue;
            }
            if (!IsValid(key))
            {
                throw new InvalidArgumentException(
                    $"Extensions key \"{key}\" is not a valid namespace (§21.1).");
            }
        }
    }
}
