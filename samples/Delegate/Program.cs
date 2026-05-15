// SPDX-License-Identifier: Apache-2.0
// samples/Delegate: parent agent submits a child job and emits a `delegate` event linking them.
// Spec: §10, §13.2.
using Arcp.Client;
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
var handle = await client.SubmitAsync("parent");
var res = await handle.Result;
Console.WriteLine($"parent: {res.FinalStatus}");
return 0;
