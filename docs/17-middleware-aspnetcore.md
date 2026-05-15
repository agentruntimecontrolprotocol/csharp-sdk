---
title: ASP.NET Core middleware
sdk: csharp
spec_sections: ["§4.1"]
order: 17
kind: reference
---

`Arcp.AspNetCore` mounts an `ArcpServer` on Kestrel via `IEndpointRouteBuilder.MapArcp()`.

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
});
server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input));

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(server);
var app = builder.Build();
app.UseWebSockets();
app.MapArcp(server, o =>
{
    o.Path = "/arcp";
    o.AllowedHosts = new[] { "localhost", "myapp.example.com" };
});
app.Run("http://127.0.0.1:7777");
```

## Options

| Property        | Default     | Purpose |
| --------------- | ----------- | ------- |
| `Path`          | `/arcp`     | URL pattern. |
| `AllowedHosts`  | `null` (allow all) | DNS-rebind defense — compared against `Request.Host.Host` lowercase. |

## DNS-rebind defense

`AllowedHosts` should always be set in production. The middleware rejects unmatched hosts with `403 Forbidden` **before** `AcceptWebSocketAsync`, so an attacker cannot burn the upgrade.

## Heartbeat collision

ASP.NET Core's WebSocket middleware sends a TCP-level keepalive ping every 2 minutes by default. ARCP's `session.ping` / `session.pong` is an application-layer heartbeat with a separate sequence-number exclusion rule. Set `KeepAliveInterval = Timeout.InfiniteTimeSpan` on `app.UseWebSockets(...)` so the two layers don't race.

```csharp
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = Timeout.InfiniteTimeSpan });
```
