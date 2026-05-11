// Stand-in for the Anthropic tool-use loop. Real version: an Anthropic
// client with a system prompt yielding one LLMStep per turn.
using System.Runtime.CompilerServices;

namespace ARCP.Samples.Leases;

public sealed record ToolCall(IReadOnlyList<string> Argv, string Reason);

public sealed record LLMStep(string Thought, ToolCall? ToolCall = null, string? Final = null);

internal static class Agent
{
#pragma warning disable CS1998
    public static async IAsyncEnumerable<LLMStep> LlmLoop(
        string userRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
        yield break;
    }
#pragma warning restore CS1998
}
