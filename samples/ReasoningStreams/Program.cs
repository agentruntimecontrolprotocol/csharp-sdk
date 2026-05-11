// Primary emits reasoning; mirror peer subscribes, critiques back.
using System.Text.Json;
using System.Threading.Channels;
using ARCP.Client;
using ARCP.Ids;
using ARCP.Messages.Execution;
using ARCP.Messages.Streaming;
using ARCP.Messages.Subscriptions;
using ARCP.Samples.ReasoningStreams;
using static ARCP.Samples.ReasoningStreams.ClientStubs;
using Env = ARCP.Envelope.Envelope;

const int MaxDepth = 3;
const int TokenBudget = 8_000;

// Primary side -----------------------------------------------------------

static async Task<string> RunPrimaryAsync(
    ARCPClient client,
    string request,
    ChannelReader<Critique> inboundCritiques)
{
    StreamId streamId = new($"str_{Guid.NewGuid():N}"[..14]);
    await Send(client, Envelope(
        client,
        "stream.open",
        new StreamOpen(StreamKind.Thought),
        streamId: streamId));

    Critique? last = null;
    string answer = string.Empty;
    for (int step = 0; step < MaxDepth; step++)
    {
        answer = await Agents.PrimaryStepAsync(request, last);
        await Send(client, Envelope(
            client,
            "stream.chunk",
            new StreamChunk { Sequence = step, Role = "assistant_thought", Content = answer },
            streamId: streamId));
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            last = await inboundCritiques.ReadAsync(timeout.Token);
            if (last.Severity == "halt") break;
        }
        catch (OperationCanceledException)
        {
            last = null;
        }
    }
    return answer;
}

// Mirror side (a peer runtime, NOT a pure observer — it both reads the
// thought stream AND delegates critique events back) --------------------

static async Task<SubscriptionId> SubscribeThoughtsAsync(ARCPClient mirror, SessionId targetSessionId)
{
    Env accepted = await Request(
        mirror,
        Envelope(mirror, "subscribe", new Subscribe(new SubscribeFilter
        {
            SessionId = [targetSessionId.Value],
            Types = ["stream.chunk"],
        })),
        timeout: TimeSpan.FromSeconds(10));
    return ((SubscribeAccepted)accepted.Payload).SubscriptionId;
}

static bool IsThought(Env env)
{
    if (env.Type != "stream.chunk") return false;
    StreamChunk chunk = (StreamChunk)env.Payload;
    return chunk.Role == "assistant_thought";
}

static async Task RunMirrorAsync(ARCPClient mirror, SessionId targetSessionId)
{
    SubscriptionId subId = await SubscribeThoughtsAsync(mirror, targetSessionId);
    int spent = 0;
    try
    {
        await foreach (Env env in Events(mirror))
        {
            if (env.Type != "subscribe.event") continue;
            SubscribeEvent wrap = (SubscribeEvent)env.Payload;
            Env innerEnv = EnvelopeStubs.FromWire(wrap.Event);
            if (!IsThought(innerEnv)) continue;
            if (spent >= TokenBudget)
            {
                // Tear down cleanly: runtime stops paying for events
                // we'll never act on.
                await Send(mirror, Envelope(mirror, "unsubscribe", new Unsubscribe(subId)));
                return;
            }

            StreamChunk innerChunk = (StreamChunk)innerEnv.Payload;
            (string severity, string summary, string? suggestion, int consumed) =
                await Agents.CritiqueThoughtAsync(innerChunk.Content ?? string.Empty);
            spent += consumed;
            Critique critique = new(
                TargetThoughtSequence: (int)innerChunk.Sequence,
                Severity: severity,
                Summary: summary,
                Suggestion: suggestion,
                ConsumedTokens: consumed);
            await Send(mirror, Envelope(
                mirror,
                "agent.delegate",
                new AgentDelegate(
                    Target: targetSessionId.Value,
                    Task: "consume_critique",
                    Context: JsonSerializer.SerializeToElement(new { critique }))));
        }
    }
    finally
    {
        await Send(mirror, Envelope(mirror, "unsubscribe", new Unsubscribe(subId)));
    }
}

ARCPClient primary = null!; // transport, identity, auth elided
ARCPClient mirror = null!;
await Open(primary);
await Open(mirror);

Channel<Critique> inbound = Channel.CreateUnbounded<Critique>();

_ = Task.Run(async () =>
{
    await foreach (Env env in Events(primary))
    {
        if (env.Type == "agent.delegate")
        {
            AgentDelegate del = (AgentDelegate)env.Payload;
            if (del.Context is { } ctx
                && ctx.TryGetProperty("critique", out JsonElement critique))
            {
                Critique c = critique.Deserialize<Critique>()!;
                await inbound.Writer.WriteAsync(c);
            }
        }
    }
});

_ = Task.Run(() => RunMirrorAsync(mirror, primary.SessionId ?? new SessionId("sess_unknown")));

string answer = await RunPrimaryAsync(
    primary,
    request: "Argue both sides: serializable vs snapshot iso?",
    inboundCritiques: inbound.Reader);
Console.WriteLine(answer);

await primary.CloseAsync();
await mirror.CloseAsync();
