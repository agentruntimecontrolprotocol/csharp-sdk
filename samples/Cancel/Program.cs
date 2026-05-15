// SPDX-License-Identifier: Apache-2.0
// samples/Cancel: client cancels a running job; final envelope is job.error{final_status:"cancelled"}.
// Spec: §7.4.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "cancel", Version = "1.0.0" },
});
server.RegisterAgent("sleeper", async (ctx, ct) =>
{
    await Task.Delay(TimeSpan.FromSeconds(60), ct);
    return null;
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "cancel-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("sleeper");
await Task.Delay(200);
await handle.CancelAsync(reason: "user-requested");
var res = await handle.Result;
Console.WriteLine($"cancelled status={res.FinalStatus}");
return res.FinalStatus == "cancelled" ? 0 : 1;
