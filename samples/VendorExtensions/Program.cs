// SPDX-License-Identifier: Apache-2.0
// samples/VendorExtensions: vendor-namespaced event kinds round-trip without runtime support.
// Spec: §8.2, §15.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "vendor-ext", Version = "1.0.0" },
});
server.RegisterAgent("vendor", async (ctx, ct) =>
{
    await ctx.EmitEventAsync("x-vendor.acme.thumbnail", new { url = "https://example.invalid/t.png" }, ct);
    return "ok";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "vendor-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("vendor");
_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
    {
        if (ev.Kind.StartsWith("x-vendor."))
            Console.WriteLine($"vendor event: {ev.Kind} body={ev.Body.GetRawText()}");
    }
});
await handle.Result;
return 0;
