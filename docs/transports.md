# Transports

ARCP is transport-agnostic. This SDK ships three implementations of
`ITransport`, covering every scenario from network deployments to
in-process testing.

| Transport            | Spec | Use case |
| -------------------- | ---- | -------- |
| `WebSocketTransport` | §4.1 | Network deployments. UTF-8 text frames; one JSON envelope per frame. |
| `StdioTransport`     | §4.2 | Child-process agents. Newline-delimited JSON over an arbitrary `Stream` pair. |
| `MemoryTransport`    | n/a  | Tests and same-process runtime hosting. |

## WebSocket

```csharp
var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://host:port/arcp"), CancellationToken.None);
ITransport transport = new WebSocketTransport(ws, ownsSocket: true);
```

Pass `ownsSocket: true` when you want `transport.DisposeAsync()` to
close the underlying socket. Pass `false` if another owner controls it.

The server side is provided by `Arcp.AspNetCore`:

```csharp
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = Timeout.InfiniteTimeSpan, // let ARCP heartbeat handle liveness
});
app.MapArcp(server, o => o.Path = "/arcp");
```

See [Arcp.AspNetCore](./projects/Arcp.AspNetCore.md) for full options.

## stdio

Use `StdioTransport` when the runtime runs as a child process of the
client, or when the client is itself a child process.

**Agent (child process):**

```csharp
// In Program.cs of the agent:
var server = new ArcpServer(options);
server.RegisterAgent("my-agent", handler);
var transport = StdioTransport.FromConsole();
await server.AcceptAsync(transport);
```

`StdioTransport.FromConsole()` reads from `Console.OpenStandardInput()`
and writes to `Console.OpenStandardOutput()`. Agent code MUST write
logs to `stderr` — any non-envelope byte on `stdout` will corrupt the
channel.

**Parent process spawning a child agent:**

```csharp
using var proc = Process.Start(new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = "run --project ./MyAgent",
    RedirectStandardInput  = true,
    RedirectStandardOutput = true,
    UseShellExecute        = false,
});
var transport = new StdioTransport(
    proc!.StandardOutput.BaseStream,
    proc.StandardInput.BaseStream);
await using var client = await ArcpClient.ConnectAsync(transport, options);
```

## MemoryTransport

`MemoryTransport.Pair()` returns two linked transports — one for the
client and one for the server — backed by in-process channels. No
sockets, no serialization round-trip delay.

```csharp
var (clientTransport, serverTransport) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverTransport);
await using var client = await ArcpClient.ConnectAsync(clientTransport, options);
```

Preferred transport for:

- Unit and integration tests.
- Samples and quickstarts.
- Same-process runtimes where network overhead is undesirable.

## Adding tracing to any transport

Wrap any `ITransport` with `WithTracing()` from `Arcp.Otel`:

```csharp
using Arcp.Otel;

var traced = transport.WithTracing();
```

The wrapper emits one OTel span per envelope (send and receive) and
injects/extracts the W3C `traceparent` via
`envelope.extensions["x-vendor.opentelemetry.tracecontext"]`.

See [Observability](./guides/observability.md) and the
[Arcp.Otel](./projects/Arcp.Otel.md) project page.
