# Getting started

## Install

```sh
dotnet add package Arcp
```

`Arcp` is a meta-package that pulls in `Arcp.Core`, `Arcp.Client`, and
`Arcp.Runtime`. Add individual packages for middleware:

```sh
dotnet add package Arcp.AspNetCore   # Kestrel WebSocket endpoint
dotnet add package Arcp.Otel         # OpenTelemetry tracing
dotnet add package Arcp.Hosting      # Worker-process DI registration
dotnet add package Arcp.Cli          # arcp serve / submit / version
```

## In-process quickstart (MemoryTransport)

The fastest path: runtime and client in the same process over an
in-memory channel.

```csharp
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "demo", Version = "1.0.0" },
});
server.RegisterAgent("echo", (ctx, ct) =>
    Task.FromResult<object?>(ctx.Input));

var (clientTransport, serverTransport) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverTransport);

await using var client = await ArcpClient.ConnectAsync(
    clientTransport,
    new ArcpClientOptions
    {
        Client = new ClientInfo { Name = "demo-client", Version = "1.0.0" },
    });

var handle = await client.SubmitAsync("echo", new { hi = 1 });
var result = await handle.Result;
// result.Success == true
// result.Result.FinalStatus == "success"
```

## Over WebSocket (Kestrel + `Arcp.AspNetCore`)

Server:

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
});
server.RegisterAgent("echo", (ctx, ct) =>
    Task.FromResult<object?>(ctx.Input));

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(server);
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = Timeout.InfiniteTimeSpan,
});
app.MapArcp(server, o => o.Path = "/arcp");
app.Run("http://127.0.0.1:7777");
```

Client:

```csharp
var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://127.0.0.1:7777/arcp"), ct);
ITransport transport = new WebSocketTransport(ws, ownsSocket: true);

await using var client = await ArcpClient.ConnectAsync(
    transport,
    new ArcpClientOptions
    {
        Client = new ClientInfo { Name = "my-app", Version = "1.0.0" },
        Token = "tok",
    });
```

Runnable variants live in [`samples/SubmitAndStream/`](../samples/SubmitAndStream/)
and [`samples/AspNetCore/`](../samples/AspNetCore/).

## Next steps

- [Architecture](./architecture.md) — how the packages fit together.
- [Transports](./transports.md) — WebSocket vs stdio vs in-memory.
- [Sessions guide](./guides/sessions.md) — hello/welcome, feature negotiation, heartbeat.
- [Jobs guide](./guides/jobs.md) — submit, observe, cancel, budget.
