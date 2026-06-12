// SPDX-License-Identifier: Apache-2.0
// samples/Delegate: parent agent submits a child job and emits a `delegate` event linking them.
// Spec: §10, §13.2.
using Arcp.Client;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "delegate", Version = "1.0.0" },
});
server.RegisterAgent("child", (ctx, ct) => Task.FromResult<object?>("child-done"));
server.RegisterAgent("parent", async (ctx, ct) =>
{
    await ctx.DelegateAsync("child_job_001", "child", new { from = "parent" }, ct);
    await ctx.LogAsync("info", "parent finished after delegating", ct);
    return "parent-done";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "delegate-client", Version = "1.0.0" },
});
// Spec §9.3 deny-by-default: agent.delegate must be covered by the lease for the parent to delegate.
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    ["agent.delegate"] = new[] { "*" },
});
var handle = await client.SubmitAsync("parent", leaseRequest: lease);
var res = await handle.Result;
Console.WriteLine($"parent: {res.FinalStatus}");
return 0;
