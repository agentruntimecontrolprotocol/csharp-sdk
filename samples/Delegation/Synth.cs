// Final-pass synthesizer. Real version: an Anthropic call that folds
// successful subagent outputs into prose, ignoring failed peers.
namespace ARCP.Samples.Delegation;

internal static class Synth
{
    public static string Synthesize(string request, IReadOnlyList<DelegatedJob> jobs) =>
        throw new NotImplementedException();
}
