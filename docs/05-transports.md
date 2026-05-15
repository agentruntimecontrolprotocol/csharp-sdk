---
title: Transports
sdk: csharp
spec_sections: ["§4.1", "§4.2"]
order: 5
kind: reference
---

ARCP is transport-agnostic. This SDK ships three implementations of `ITransport`:

| Transport             | Spec | Use case |
| --------------------- | ---- | -------- |
| `WebSocketTransport`  | §4.1 | Network deployments. UTF-8 text frames; one JSON envelope per frame. |
| `StdioTransport`      | §4.2 | Child-process agents. Newline-delimited JSON over an arbitrary `Stream` pair. |
| `MemoryTransport`     | n/a  | Tests and same-process runtime hosting. `MemoryTransport.Pair()` returns a paired client/server. |

## WebSocket

```csharp
var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://host:port/arcp"), CancellationToken.None);
ITransport transport = new WebSocketTransport(ws, ownsSocket: true);
```

The server side is provided by `Arcp.AspNetCore.MapArcp("/arcp")` — see [`17-middleware-aspnetcore.md`](./17-middleware-aspnetcore.md).

## stdio

```csharp
ITransport transport = StdioTransport.FromConsole();
```

For a parent process spawning a child agent:

```csharp
using var proc = Process.Start(new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = "run --project ./MyAgent",
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
});
var transport = new StdioTransport(proc.StandardOutput.BaseStream, proc.StandardInput.BaseStream);
```

## Memory

```csharp
var (clientTransport, serverTransport) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverTransport);
await using var client = await ArcpClient.ConnectAsync(clientTransport, options);
```

`MemoryTransport.Pair()` is the supported test transport (matches the TypeScript reference's `MemoryTransport.pair()`).
