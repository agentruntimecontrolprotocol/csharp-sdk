// SPDX-License-Identifier: Apache-2.0
// samples/Stdio: stdio transport — newline-delimited JSON over stdin/stdout. Spec §4.2.
// This sample mirrors the canonical MemoryTransport demo since stdio is a process boundary;
// production stdio agents wire `StdioTransport.FromConsole()` into a child process.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "stdio", Version = "1.0.0" },
});
server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input));

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "stdio-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("echo", "hello-stdio");
var res = await handle.Result;
Console.WriteLine($"stdio result={res.FinalStatus}");
return 0;
