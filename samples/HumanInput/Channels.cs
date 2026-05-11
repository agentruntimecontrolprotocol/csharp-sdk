// Per-destination channel adapters. Real versions wrap ntfy.sh, SES, and
// the Slack web API. Each returns a value matching the request's
// `response_schema`.
using System.Text.Json;

namespace ARCP.Samples.HumanInput;

internal delegate Task<JsonElement> ChannelResponse(string prompt, JsonElement schema);

internal static class Channels
{
    public static Task<JsonElement> NtfyPhoneAsync(string prompt, JsonElement schema) =>
        throw new NotImplementedException();

    public static Task<JsonElement> EmailOncallAsync(string prompt, JsonElement schema) =>
        throw new NotImplementedException();

    public static Task<JsonElement> SlackOpsAsync(string prompt, JsonElement schema) =>
        throw new NotImplementedException();

    public static IReadOnlyDictionary<string, ChannelResponse> Registry { get; } =
        new Dictionary<string, ChannelResponse>
        {
            ["ntfy:phone"] = NtfyPhoneAsync,
            ["email:oncall"] = EmailOncallAsync,
            ["slack:ops"] = SlackOpsAsync,
        };
}
