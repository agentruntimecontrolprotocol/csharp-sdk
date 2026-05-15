// SPDX-License-Identifier: Apache-2.0
// samples/AckBackpressure: client sends session.ack to declare its highest-processed seq; runtime
// emits status{phase:"back_pressure"} when lag exceeds the threshold. Spec §6.5, §13.2.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "ack-backpressure", Version = "1.0.0" },
    BackPressureThreshold = 10,
});
server.RegisterAgent("chatty", async (ctx, ct) =>
{
    for (var i = 0; i < 30; i++) await ctx.LogAsync("info", $"chatty {i}", ct);
    return "done";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "ack-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("chatty");
var seen = 0;
_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
    {
        seen++;
        if (seen % 5 == 0) await client.AckAsync(ev.EventSeq);
    }
});
await handle.Result;
Console.WriteLine($"events processed={seen}");
return 0;
