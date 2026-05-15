// SPDX-License-Identifier: Apache-2.0
// samples/Heartbeat: client and runtime exchange session.ping/session.pong without advancing
// event_seq. Spec §6.4.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "heartbeat", Version = "1.0.0" },
    HeartbeatIntervalSec = 1,
});
server.RegisterAgent("idle", async (ctx, ct) =>
{
    await Task.Delay(TimeSpan.FromSeconds(2), ct);
    return "done";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "heartbeat-client", Version = "1.0.0" },
});
Console.WriteLine($"heartbeat_interval_sec={client.HeartbeatIntervalSec}");
var handle = await client.SubmitAsync("idle");
await handle.Result;
Console.WriteLine($"last seq received={client.LastReceivedSeq}");
return 0;
