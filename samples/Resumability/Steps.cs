// Step bodies. Real version: a graph node per step (Anthropic call for
// plan / synth / critique / finalize, retriever for gather).
using System.Collections.Generic;
using ARCP.Client;
using ARCP.Ids;

namespace ARCP.Samples.Resumability;

internal static class Steps
{
    public static Task<object> RunStepAsync(
        ARCPClient client,
        JobId jobId,
        string step,
        IReadOnlyDictionary<string, object> inputs) =>
        throw new NotImplementedException();
}
