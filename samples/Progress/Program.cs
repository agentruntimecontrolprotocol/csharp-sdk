// SPDX-License-Identifier: Apache-2.0
// samples/Progress: agent emits progress events; client renders a simple bar. Spec §8.2.1.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "progress", Version = "1.0.0" },
});
server.RegisterAgent("indexer", async (ctx, ct) =>
{
    for (long i = 1; i <= 5; i++)
    {
        await ctx.ProgressAsync(i, total: 5, units: "files", message: $"Indexing file {i}/5", ct);
        await Task.Delay(50, ct);
    }
    return "indexed 5 files";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "progress-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("indexer");
_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
    {
        if (ev.Kind != "progress") continue;
        var body = ev.BodyAs<ProgressBody>()!;
        Console.WriteLine($"  {body.Current}/{body.Total} {body.Units}: {body.Message}");
    }
});
await handle.Result;
return 0;
