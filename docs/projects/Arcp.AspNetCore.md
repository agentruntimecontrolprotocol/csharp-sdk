# Arcp.AspNetCore

`Arcp.AspNetCore` mounts an `ArcpServer` on Kestrel via a single
`MapArcp()` extension on `IEndpointRouteBuilder`. It handles WebSocket
upgrade, allowed-host validation, and lifetime wiring.

```sh
dotnet add package Arcp.AspNetCore
```

## Quick start

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
    Auth    = new StaticBearerVerifier(("tok-demo", new AuthPrincipal("alice"))),
});
server.RegisterAgent("echo", async (ctx, ct) => ctx.Input);

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = Timeout.InfiniteTimeSpan,  // see below
});
app.MapArcp(server, o =>
{
    o.Path         = "/arcp";
    o.AllowedHosts = new[] { "localhost", "myapp.example.com" };
});

app.Run("http://127.0.0.1:7777");
```

## ArcpEndpointOptions

| Property       | Default            | Purpose                                                   |
| -------------- | ------------------ | --------------------------------------------------------- |
| `Path`         | `"/arcp"`          | URL pattern registered with the endpoint router.          |
| `AllowedHosts` | `null` (allow all) | DNS-rebind defense — matched against `Request.Host.Host`. |

## DNS-rebind defense

`AllowedHosts` should always be set in production. The middleware compares
the incoming `Host` header (lowercase) against the list and returns
`403 Forbidden` **before** `AcceptWebSocketAsync`, so an attacker cannot
consume the WebSocket upgrade budget.

```csharp
o.AllowedHosts = new[] { "api.example.com" };
```

## Heartbeat collision

ASP.NET Core's WebSocket middleware sends a TCP-level keepalive ping on its
own timer. ARCP's `session.ping` / `session.pong` is a separate
application-layer heartbeat with its own sequence-number exclusion rule. To
prevent the two from racing, disable the ASP.NET Core ping:

```csharp
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = Timeout.InfiniteTimeSpan });
```

## Multiple endpoints

Call `MapArcp` more than once to expose the same server on several paths or
with different allowed-host policies:

```csharp
app.MapArcp(server, o => { o.Path = "/arcp";          o.AllowedHosts = new[] { "internal.corp" }; });
app.MapArcp(server, o => { o.Path = "/arcp-external";  o.AllowedHosts = new[] { "api.example.com" }; });
```

## Related

- [Arcp.Runtime](./Arcp.Runtime.md) — `ArcpServer` configuration.
- [Arcp.Otel](./Arcp.Otel.md) — transport instrumentation.
- [Arcp.Hosting](./Arcp.Hosting.md) — `IHostedService` / DI integration.
- [Troubleshooting — 403 Forbidden](../troubleshooting.md#websocket-upgrade-returns-403-forbidden) — allowed-host failures.
- [Troubleshooting — HEARTBEAT_LOST](../troubleshooting.md#job-is-cancelled-with-heartbeat_lost) — keepalive collision.
