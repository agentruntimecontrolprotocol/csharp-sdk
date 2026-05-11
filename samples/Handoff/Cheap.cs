// Cheap-tier inference. Real version: a Haiku-tier call with a system
// prompt asking for a `Confidence: X.XX` line, then heuristics for the score.
namespace ARCP.Samples.Handoff;

internal static class Cheap
{
    public static Task<(string Answer, double Confidence)> AttemptAsync(string prompt) =>
        throw new NotImplementedException();
}
