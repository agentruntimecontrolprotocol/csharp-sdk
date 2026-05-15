---
title: Quickstart
sdk: csharp
spec_sections: ["§6", "§7"]
order: 1
kind: guide
---

## In-process (MemoryTransport)

```csharp
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "demo", Version = "1.0.0" },
});
server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input));

var (client, srv) = MemoryTransport.Pair();
_ = server.AcceptAsync(srv);

await using var arcp = await ArcpClient.ConnectAsync(client, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "demo-client", Version = "1.0.0" },
});
var handle = await arcp.SubmitAsync("echo", new { hi = 1 });
var result = await handle.Result;
```

## Over WebSocket (Kestrel + Arcp.AspNetCore)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(server);
var app = builder.Build();
app.UseWebSockets();
app.MapArcp(server, o => o.Path = "/arcp");
app.Run("http://127.0.0.1:7777");
```

Client side:

```csharp
var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://127.0.0.1:7777/arcp"), CancellationToken.None);
var transport = new WebSocketTransport(ws);
await using var arcp = await ArcpClient.ConnectAsync(transport, options);
```

Runnable variants: [`samples/SubmitAndStream/`](../samples/SubmitAndStream/) and [`samples/AspNetCore/`](../samples/AspNetCore/).
