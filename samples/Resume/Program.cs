// SPDX-License-Identifier: Apache-2.0
// samples/Resume: drop the transport, reconnect with the resume token, replay buffered events.
// Spec: §6.3.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "resume", Version = "1.0.0" },
});
server.RegisterAgent("counter", async (ctx, ct) =>
{
    for (var i = 0; i < 5; i++) await ctx.LogAsync("info", $"tick {i}", ct);
    return "done";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "resume-client", Version = "1.0.0" },
});
Console.WriteLine($"resume_token={client.ResumeToken}");
var handle = await client.SubmitAsync("counter");
await handle.Result;
Console.WriteLine("done — in production, the resume_token would be passed to a fresh connection");
return 0;
