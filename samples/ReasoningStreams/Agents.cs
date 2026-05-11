// Primary + critic LLM stand-ins.
namespace ARCP.Samples.ReasoningStreams;

public sealed record Critique(
    int TargetThoughtSequence,
    string Severity,
    string Summary,
    string? Suggestion,
    int ConsumedTokens);

internal static class Agents
{
    // Real version: an LLM call that folds the critique into the prompt
    // when present.
    public static Task<string> PrimaryStepAsync(string request, Critique? priorCritique) =>
        throw new NotImplementedException();

    // Returns (severity, summary, suggestion, tokens_consumed).
    // severity ∈ ["nudge", "warn", "halt"].
    public static Task<(string Severity, string Summary, string? Suggestion, int Consumed)>
        CritiqueThoughtAsync(string thought) =>
        throw new NotImplementedException();
}
