# Arcp.Hosting

`Arcp.Hosting` is a small DI helper for registering an `ArcpServer`
inside `Microsoft.Extensions.DependencyInjection`. It does not provide
an `IHostedService`; transport acceptance is driven by the transport
package you choose (`Arcp.AspNetCore` for HTTP/WebSocket, or your own
loop calling `server.AcceptAsync` for stdio / TCP / custom).

```sh
dotnet add package Arcp.Hosting
```

## Registration

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArcpRuntime(o =>
{
    o.Runtime              = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" };
    o.Auth                 = new StaticBearerVerifier(("tok-demo", new AuthPrincipal("alice")));
    o.HeartbeatIntervalSec = 30;
});
```

`AddArcpRuntime` registers `ArcpServer` as a singleton bound to
`ArcpServerOptions` via the standard options pattern. Resolve the
singleton wherever you need it:

```csharp
public sealed class AgentRegistrar(ArcpServer server)
{
    public void Register()
    {
        server.RegisterAgent("echo", async (ctx, ct) => ctx.Input);
    }
}
```

## With ASP.NET Core

When the runtime is hosted over HTTP, combine `AddArcpRuntime` with
`MapArcp` from `Arcp.AspNetCore`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddArcpRuntime(o => { /* ... */ });

var app = builder.Build();

var server = app.Services.GetRequiredService<ArcpServer>();
server.RegisterAgent("echo", async (ctx, ct) => ctx.Input);

app.UseWebSockets();
app.MapArcp(server);
app.Run("http://127.0.0.1:7777");
```

## Non-HTTP hosts

For stdio, named pipes, or custom TCP, write a small `IHostedService`
that creates the transport and calls `server.AcceptAsync(transport, ct)`
in a loop. `AddArcpRuntime` gives you the singleton; the acceptance
loop is yours.

## Related

- [Arcp.Runtime](./Arcp.Runtime.md) — `ArcpServer` options reference.
- [Arcp.AspNetCore](./Arcp.AspNetCore.md) — Kestrel / WebSocket hosting.
- [Auth guide](../guides/auth.md) — bearer token verification.
