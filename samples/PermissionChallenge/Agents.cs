// Generator + reviewer stand-ins. Real version: AutoGen-equivalent
// AssistantAgents.
using Env = ARCP.Envelope.Envelope;

namespace ARCP.Samples.PermissionChallenge;

public sealed record Patch(string Diff);

public sealed record ReviewVerdict(bool Grant, string Reason);

internal static class Agents
{
    public static Task<Patch> ProposeAsync(string ticket, string? priorDenial) =>
        throw new NotImplementedException();

    public static Task<ReviewVerdict> ReviewAsync(string ticket, Env request) =>
        throw new NotImplementedException();
}
