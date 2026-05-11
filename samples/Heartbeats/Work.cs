// Worker work. Real version: a CrewAI-equivalent crew sized per role,
// kicked off via Task.Run.
using System.Text.Json;

namespace ARCP.Samples.Heartbeats;

internal static class Work
{
    public static Task<JsonElement> DoWorkAsync(JsonElement payload) =>
        throw new NotImplementedException();
}
