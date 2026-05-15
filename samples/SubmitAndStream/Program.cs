// SPDX-License-Identifier: Apache-2.0
// samples/SubmitAndStream: submit a job, stream the agent's events, await the result.
// Spec: §7.1 (submit), §8.1-§8.3 (events), §13.1.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "submit-and-stream", Version = "1.0.0" },
});
server.RegisterAgent("greeter", async (ctx, ct) =>
{
    await ctx.StatusAsync("starting");
    await ctx.LogAsync("info", $"Hello, {ctx.Input}!", ct);
    await ctx.StatusAsync("complete");
    return new { greeting = $"Hello, {ctx.Input}!" };
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);

await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "submit-and-stream-client", Version = "1.0.0" },
});
Console.WriteLine($"connected session={client.SessionId}");

var handle = await client.SubmitAsync("greeter", "world");
Console.WriteLine($"accepted job={handle.JobId}");

_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
    {
        Console.WriteLine($"  event kind={ev.Kind} seq={ev.EventSeq}");
    }
});

var result = await handle.Result;
Console.WriteLine($"result success={result.Success}");
return result.Success ? 0 : 1;
