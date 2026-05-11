// Fan `human.input.request` across channels; resolve on first.
using System.Text.Json;
using ARCP.Client;
using ARCP.Messages.Human;
using ARCP.Samples.HumanInput;
using static ARCP.Samples.HumanInput.ClientStubs;
using Env = ARCP.Envelope.Envelope;

string[] destinations = ["ntfy:phone", "email:oncall", "slack:ops"];

async Task FanOutAsync(ARCPClient client, Env request)
{
    HumanInputRequest req = (HumanInputRequest)request.Payload;
    JsonElement schema = req.ResponseSchema;
    string prompt = req.Prompt;
    DateTimeOffset expiresAt = req.ExpiresAt;
    TimeSpan timeout = expiresAt - DateTimeOffset.UtcNow;
    if (timeout < TimeSpan.Zero) timeout = TimeSpan.Zero;

    using CancellationTokenSource cts = new();
    Dictionary<Task<(string Dest, JsonElement Value)>, string> tasks = new();
    foreach (string dest in destinations)
    {
        string captured = dest;
        var t = Task.Run(async () => (captured, await Channels.Registry[captured](prompt, schema)));
        tasks[t] = captured;
    }

    Task winner = await Task.WhenAny(tasks.Keys.Concat(new[] { Task.Delay(timeout, cts.Token) }));
    foreach (Task<(string, JsonElement)> t in tasks.Keys)
    {
        if (t != winner && !t.IsCompleted) cts.Cancel();
    }

    if (winner is not Task<(string Dest, JsonElement Value)> won)
    {
        // Deadline elapsed; translate timeout into the cancelled-input
        // shape (RFC §12.4).
        await Send(client, Envelope(
            client,
            "human.input.cancelled",
            new HumanInputCancelled(Code: "DEADLINE_EXCEEDED",
                Message: "no channel responded before expires_at"),
            correlationId: request.Id));
        return;
    }

    (string respondedBy, JsonElement value) = await won;
    await Send(client, Envelope(
        client,
        "human.input.response",
        new HumanInputResponse(Value: value, RespondedBy: respondedBy, RespondedAt: DateTimeOffset.UtcNow),
        correlationId: request.Id));

    // Tell the losing destinations the question is settled. Each channel
    // adapter would translate this to "delete the push" / "edit the slack
    // message to '(answered)'".
    string[] losers = tasks.Where(kv => kv.Key != winner).Select(kv => kv.Value).ToArray();
    if (losers.Length > 0)
    {
        await Send(client, Envelope(
            client,
            "human.input.cancelled",
            new HumanInputCancelled(Code: "OK", Message: $"answered elsewhere: {string.Join(",", losers)}"),
            correlationId: request.Id));
    }
}

ARCPClient client = null!; // transport, identity, auth elided
await Open(client);
HashSet<Task> runners = [];
try
{
    await foreach (Env env in Events(client))
    {
        if (env.Type == "human.input.request")
        {
            Task t = Task.Run(() => FanOutAsync(client, env));
            runners.Add(t);
            _ = t.ContinueWith(x => runners.Remove(x), TaskScheduler.Default);
        }
    }
}
finally
{
    await client.CloseAsync();
}
