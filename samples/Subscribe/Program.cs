// SPDX-License-Identifier: Apache-2.0
// samples/Subscribe: a second client attaches to a running job from another session. Spec §7.6.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "subscribe", Version = "1.0.0" },
});
server.RegisterAgent("worker", async (ctx, ct) =>
{
    for (var i = 0; i < 5; i++)
    {
        await ctx.LogAsync("info", $"step {i}", ct);
        await Task.Delay(50, ct);
    }
    return "done";
});

// Submitter
var (a, sa) = MemoryTransport.Pair();
_ = server.AcceptAsync(sa);
await using var submitter = await ArcpClient.ConnectAsync(a, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "submitter", Version = "1.0.0" },
});
var handle = await submitter.SubmitAsync("worker");

// Observer
var (b, sb) = MemoryTransport.Pair();
_ = server.AcceptAsync(sb);
await using var observer = await ArcpClient.ConnectAsync(b, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "observer", Version = "1.0.0" },
});
var sub = await observer.SubscribeAsync(handle.JobId, history: true);
Console.WriteLine($"subscribed: subscribed_from={sub.Acknowledged.Result.SubscribedFrom}");
await handle.Result;
return 0;
