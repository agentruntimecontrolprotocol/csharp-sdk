// SPDX-License-Identifier: Apache-2.0
// samples/CostBudget: cost.* metrics decrement a per-currency counter; reaching zero produces
// BUDGET_EXHAUSTED on the next operation. Spec §9.6, §13.5.
using Arcp.Client;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "cost-budget", Version = "1.0.0" },
});
server.RegisterAgent("research", async (ctx, ct) =>
{
    await ctx.ToolCallAsync("search.web", "c1", new { q = "arcp" }, ct);
    await ctx.MetricAsync("cost.search", 0.42, "USD", cancellationToken: ct);
    await ctx.MetricAsync("cost.budget.remaining", (double)ctx.Budget["USD"], "USD", cancellationToken: ct);
    await ctx.ToolCallAsync("fetch.url", "c2", new { url = "https://example.invalid/" }, ct);
    await ctx.MetricAsync("cost.fetch", 0.70, "USD", cancellationToken: ct);
    return new { remaining = ctx.Budget["USD"] };
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "budget-client", Version = "1.0.0" },
});
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    ["cost.budget"] = new[] { "USD:1.00" },
});
var handle = await client.SubmitAsync("research", leaseRequest: lease);
Console.WriteLine($"initial budget: {string.Join(",", handle.Budget!)}");
var res = await handle.Result;
Console.WriteLine($"final: {res.FinalStatus}");
return 0;
