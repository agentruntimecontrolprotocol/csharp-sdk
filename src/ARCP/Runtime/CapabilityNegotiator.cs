using ARCP.Errors;
using ARCP.Messages.Session;

namespace ARCP.Runtime;

/// <summary>
/// Negotiates the agreed capability set per RFC-0001-v2 §7.
/// </summary>
public static class CapabilityNegotiator
{
    /// <summary>
    /// Compute the agreed-upon capabilities from the client's request and the
    /// runtime's advertised set. The result is the per-flag AND of both
    /// sides; missing booleans default to <see langword="false" /> (§7).
    /// </summary>
    /// <param name="requested">The client's <c>session.open.capabilities</c>.</param>
    /// <param name="advertised">The runtime's advertised capabilities.</param>
    /// <returns>The negotiated capability set.</returns>
    /// <exception cref="UnimplementedException">
    /// If a required-but-unsupported capability is present.
    /// </exception>
    public static Capabilities Negotiate(Capabilities requested, Capabilities advertised)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(advertised);

        bool? streaming = And(requested.Streaming, advertised.Streaming);
        bool? durable = And(requested.DurableJobs, advertised.DurableJobs);
        bool? checkpoints = And(requested.Checkpoints, advertised.Checkpoints);
        bool? binary = And(requested.BinaryStreams, advertised.BinaryStreams);
        bool? handoff = And(requested.AgentHandoff, advertised.AgentHandoff);
        bool? human = And(requested.HumanInput, advertised.HumanInput);
        bool? artifacts = And(requested.Artifacts, advertised.Artifacts);
        bool? subscriptions = And(requested.Subscriptions, advertised.Subscriptions);
        bool? scheduled = And(requested.ScheduledJobs, advertised.ScheduledJobs);
        bool? interrupt = And(requested.Interrupt, advertised.Interrupt);
        bool? anonymous = And(requested.Anonymous, advertised.Anonymous);

        // Negotiate the binary encoding intersection (or null when neither side declared one).
        IReadOnlyList<string>? binaryEncoding = null;
        if (advertised.BinaryEncoding is { Count: > 0 } adv)
        {
            if (requested.BinaryEncoding is { Count: > 0 } req)
            {
                var intersection = adv.Intersect(req).ToList();
                binaryEncoding = intersection.Count > 0 ? intersection : null;
            }
            else
            {
                binaryEncoding = adv;
            }
        }

        return new Capabilities
        {
            Streaming = streaming,
            DurableJobs = durable,
            Checkpoints = checkpoints,
            BinaryStreams = binary,
            BinaryEncoding = binaryEncoding,
            AgentHandoff = handoff,
            HumanInput = human,
            Artifacts = artifacts,
            Subscriptions = subscriptions,
            ScheduledJobs = scheduled,
            Interrupt = interrupt,
            Anonymous = anonymous,
            HeartbeatIntervalSeconds = advertised.HeartbeatIntervalSeconds,
            HeartbeatRecovery = advertised.HeartbeatRecovery,
            ArtifactRetention = advertised.ArtifactRetention,
            Extensions = NegotiateExtensions(requested.Extensions, advertised.Extensions),
        };
    }

    private static bool? And(bool? a, bool? b)
    {
        if (a is null && b is null)
        {
            return null;
        }
        return (a ?? false) && (b ?? false);
    }

    private static IReadOnlyList<string>? NegotiateExtensions(
        IReadOnlyList<string>? requested,
        IReadOnlyList<string>? advertised)
    {
        if (advertised is null || advertised.Count == 0)
        {
            return null;
        }
        if (requested is null || requested.Count == 0)
        {
            return advertised;
        }
        var intersection = advertised.Intersect(requested, StringComparer.Ordinal).ToList();
        return intersection.Count == 0 ? null : intersection;
    }
}
