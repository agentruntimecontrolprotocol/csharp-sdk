# Arcp.Hosting

`Arcp.Hosting` integrates `ArcpServer` with the .NET Generic Host
(`Microsoft.Extensions.Hosting`). It registers the server as an
`IHostedService` so that `AcceptAsync` loops start and stop with the
application lifetime.

```sh
dotnet add package Arcp.Hosting
```

## Registration

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddArcp(o =>
    {
        o.Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" };
        o.Auth    = new StaticBearerVerifier(("tok-demo", new AuthPrincipal("alice")));
        o.HeartbeatIntervalSec = 30;
    })
    .AddArcpAgent("echo", async (ctx, ct) => ctx.Input)
    .AddArcpTransport<MyTransportFactory>();  // supplies ITransport per session
```

`AddArcp` registers `ArcpServer` as a singleton. `AddArcpAgent` calls
`server.RegisterAgent` during `IHostedService.StartAsync`. `AddArcpTransport`
binds the hosted transport loop.

## IHostedService lifecycle

```
StartAsync  →  transport loop begins, AcceptAsync runs per incoming connection
StopAsync   →  CancellationToken signalled, in-flight sessions drain gracefully
```

The hosted service respects `IHostApplicationLifetime.ApplicationStopping` so
`SIGTERM` / Ctrl-C triggers a clean close (`session.goodbye` sent to all
connected clients).

## With ASP.NET Core

When using `Arcp.AspNetCore`, the `MapArcp` endpoint handles session
acceptance directly — you do **not** need `Arcp.Hosting` for the server loop.
Use `Arcp.Hosting` when the transport is not HTTP (stdio, named pipe, custom
TCP) or when you want a background worker that is not tied to Kestrel.

```csharp
// Worker service (non-HTTP):
builder.Services
    .AddHostedService<ArcpWorker>()   // custom IHostedService that calls AcceptAsync
    .AddArcp(o => { /* … */ })
    .AddArcpAgent("echo", …);
```

## Dependency injection in agents

Because `ArcpServer` is a DI singleton, agent handlers can receive scoped
services by declaring them in the `AddArcpAgent` overload that accepts an
`IServiceProvider`:

```csharp
builder.Services.AddArcpAgent("invoice", async (ctx, sp, ct) =>
{
    var db = sp.GetRequiredService<IInvoiceDb>();
    var rows = await db.QueryAsync(ctx.Input.GetProperty("query").GetString(), ct);
    return new { rows };
});
```

A new `IServiceScope` is created for each job and disposed when the job
reaches a terminal state.

## Configuration via appsettings

```json
// appsettings.json
{
  "Arcp": {
    "HeartbeatIntervalSec": 30,
    "BackPressureThreshold": 500
  }
}
```

```csharp
builder.Services.AddArcp(builder.Configuration.GetSection("Arcp"));
```

## Related

- [Arcp.Runtime](./Arcp.Runtime.md) — `ArcpServer` options reference.
- [Arcp.AspNetCore](./Arcp.AspNetCore.md) — Kestrel / HTTP hosting.
- [Auth guide](../guides/auth.md) — bearer token verification.
