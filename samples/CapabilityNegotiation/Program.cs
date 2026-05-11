// Capability-driven peer routing with ordered fallback + cost rollup.
using System.Collections.Concurrent;
using System.Text.Json;
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Execution;
using ARCP.Messages.Session;
using ARCP.Messages.Telemetry;
using ARCP.Samples.CapabilityNegotiation;
using static ARCP.Samples.CapabilityNegotiation.ClientStubs;
using Env = ARCP.Envelope.Envelope;

string[] peers = ["anthropic-haiku", "anthropic-sonnet", "openai-4o", "groq-llama"];
Dictionary<string, string[]> fallbackChains = new()
{
    ["cheap_fast"] = ["groq-llama", "anthropic-haiku", "openai-4o"],
    ["balanced"] = ["anthropic-sonnet", "openai-4o", "anthropic-haiku"],
    ["deep"] = ["anthropic-sonnet"],
};
const double CostCeilingUsdPerMtok = 8.0;
const int LatencyCeilingMs = 800;
HashSet<ErrorCode> retryable =
[
    ErrorCode.ResourceExhausted,
    ErrorCode.Unavailable,
    ErrorCode.DeadlineExceeded,
    ErrorCode.Aborted,
];

static Profile ProfileFrom(Capabilities? caps)
{
    // Capabilities accepts namespaced extras alongside core booleans.
    // NOTE: §21 covers extension *messages* but not extension *capability
    // values* — load-bearing convention here.
    var extra = caps?.Additional ?? new Dictionary<string, JsonElement>();
    return new Profile(
        CostPerMtok: extra.TryGetValue("arcpx.market.cost_per_mtok.v1", out JsonElement c) ? c.GetDouble() : 0.0,
        P50LatencyMs: extra.TryGetValue("arcpx.market.p50_latency_ms.v1", out JsonElement l) ? l.GetInt32() : 0,
        ModelClass: extra.TryGetValue("arcpx.market.model_class.v1", out JsonElement m) ? m.GetString() ?? "unknown" : "unknown");
}

List<string> CandidateChain(Dictionary<string, Profile> profiles, string requestClass) =>
    (fallbackChains.TryGetValue(requestClass, out string[]? chain) ? chain : [])
        .Where(name => profiles.TryGetValue(name, out Profile? p)
            && p.CostPerMtok <= CostCeilingUsdPerMtok
            && p.P50LatencyMs <= LatencyCeilingMs)
        .ToList();

async Task<Env> InvokeWithFallbackAsync(
    Dictionary<string, ARCPClient> clients,
    List<string> chain,
    string tool,
    object arguments,
    TraceId traceId)
{
    // Walk the chain. Retryable error → next peer; otherwise raise.
    ARCPException? last = null;
    foreach (string name in chain)
    {
        ARCPClient client = clients[name];
        try
        {
            Env reply = await Request(
                client,
                Envelope(
                    client,
                    "tool.invoke",
                    new ToolInvoke(Tool: tool, Arguments: JsonSerializer.SerializeToElement(arguments)),
                    traceId: traceId,
                    extensions: new Dictionary<string, JsonElement>
                    {
                        ["arcpx.market.peer.v1"] = JsonSerializer.SerializeToElement(name),
                    }),
                timeout: TimeSpan.FromSeconds(30));
            if (reply.Type != "tool.error") return reply;
            ToolError err = (ToolError)reply.Payload;
            last = new ARCPException(err.Code, err.Message);
            if (retryable.Contains(err.Code)) continue;
            throw last;
        }
        catch (ARCPException exc)
        {
            last = exc;
            if (retryable.Contains(exc.Code)) continue;
            throw;
        }
    }
    throw last ?? new ARCPException(ErrorCode.Unavailable, "no peers available");
}

void ConsumeMetric(Env env, ConcurrentDictionary<string, Usage> totals)
{
    if (env.Type != "metric") return;
    Metric m = (Metric)env.Payload;
    string tenant = m.Dims?.GetValueOrDefault("tenant") ?? "unknown";
    Usage u = totals.GetOrAdd(tenant, _ => new Usage());
    if (m.Name == ReservedMetrics.TokensUsed)
    {
        string? kind = m.Dims?.GetValueOrDefault("kind");
        if (kind == "input") u.TokensIn += (long)m.Value;
        else if (kind == "output") u.TokensOut += (long)m.Value;
    }
    else if (m.Name == ReservedMetrics.CostUsd)
    {
        u.CostUsd += m.Value;
        string peer = m.Dims?.GetValueOrDefault("peer") ?? "unknown";
        u.ByPeer[peer] = u.ByPeer.GetValueOrDefault(peer) + m.Value;
    }
}

Dictionary<string, ARCPClient> clients = new();
Dictionary<string, Profile> profiles = new();
foreach (string name in peers)
{
    ARCPClient c = null!; // transport per peer URL, identity, auth elided
    SessionAccepted accepted = await Open(c);
    clients[name] = c;
    // Marketplace fields ride on the negotiated capabilities; no extra
    // round trip to learn cost / latency / class.
    profiles[name] = ProfileFrom(accepted.Capabilities);
}

ConcurrentDictionary<string, Usage> totals = new();
using CancellationTokenSource cts = new();

List<Task> drains = clients.Values.Select(c => Task.Run(async () =>
{
    await foreach (Env env in Events(c, cts.Token))
    {
        ConsumeMetric(env, totals);
    }
})).ToList();

List<string> chosen = CandidateChain(profiles, "balanced");
TraceId traceId = new($"trace_{Guid.NewGuid():N}"[..18]);
Env reply2 = await InvokeWithFallbackAsync(
    clients, chosen, tool: "chat.completion",
    arguments: new { prompt = "Hello", tenant = "acme-corp" },
    traceId);
string? winner = reply2.Extensions?.GetValueOrDefault("arcpx.market.peer.v1").GetString();
Console.WriteLine($"chosen={winner}");
foreach (var (tenant, usage) in totals)
{
    Console.WriteLine($"usage[{tenant}] = ${usage.CostUsd:F4} ({usage.TokensIn}+{usage.TokensOut} tok)");
}

await cts.CancelAsync();
foreach (Task d in drains)
{
    try { await d; } catch (OperationCanceledException) { }
}
foreach (ARCPClient c in clients.Values) await c.CloseAsync();
