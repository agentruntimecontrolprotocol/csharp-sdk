// Boot three Observer clients on a single producing session.
using ARCP.Client;
using ARCP.Ids;
using ARCP.Messages.Subscriptions;
using ARCP.Samples.Subscriptions;
using ARCP.Samples.Subscriptions.Sinks;
using static ARCP.Samples.Subscriptions.ClientStubs;
using Env = ARCP.Envelope.Envelope;

string[] stdoutTypes = ["log", "job.started", "job.progress", "job.completed", "job.failed", "tool.error"];
string[] otlpTypes = ["metric", "trace.span"];

static async Task<SubscriptionId> SubscribeAsync(
    ARCPClient client,
    SessionId target,
    string[]? types)
{
    SubscribeFilter filter = new()
    {
        SessionId = [target.Value],
        Types = types,
    };
    Env accepted = await Request(client, Envelope(client, "subscribe", new Subscribe(filter)));
    return ((SubscribeAccepted)accepted.Payload).SubscriptionId;
}

static Env? UnwrapEvent(Env envelope)
{
    if (envelope.Type != "subscribe.event") return null;
    SubscribeEvent inner = (SubscribeEvent)envelope.Payload;
    return EnvelopeStubs.FromWire(inner.Event);
}

static async Task UnsubscribeAsync(ARCPClient client, SubscriptionId id) =>
    await Send(client, Envelope(client, "unsubscribe", new Unsubscribe(id)));

static async Task AttachAsync(string[]? types, Func<Env, Task> handler)
{
    ARCPClient client = null!; // transport, identity, auth elided
    await Open(client);
    SubscriptionId subId = await SubscribeAsync(client, target: new SessionId("sess_target"), types);
    try
    {
        await foreach (Env env in Events(client))
        {
            Env? inner = UnwrapEvent(env);
            if (inner is not null)
            {
                await handler(inner);
            }
        }
    }
    finally
    {
        await UnsubscribeAsync(client, subId);
        await client.CloseAsync();
    }
}

StdoutSink stdout = new();
OtlpSink otlp = new(endpoint: "https://otlp.local:4318");
await using SqliteSink sqlite = new(path: "replay.sqlite");

await Task.WhenAll(
    AttachAsync(stdoutTypes, stdout.HandleAsync),
    AttachAsync(types: null, sqlite.HandleAsync),
    AttachAsync(otlpTypes, otlp.HandleAsync));
