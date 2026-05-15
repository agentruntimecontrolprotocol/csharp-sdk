---
title: Runtime
sdk: csharp
spec_sections: ["§6.2", "§7.3"]
order: 4
kind: reference
---

`ArcpServer` accepts ARCP sessions on the runtime side and runs registered agents.

## Configure

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
    Auth    = new StaticBearerVerifier(("tok-demo", new AuthPrincipal("alice"))),
    HeartbeatIntervalSec = 30,
    BackPressureThreshold = 1000,
});
```

## Register agents

```csharp
server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input));

server.RegisterAgentVersion("code-refactor", "1.0.0", new MyCodeRefactor());
server.RegisterAgentVersion("code-refactor", "2.0.0", new MyCodeRefactor());
server.SetDefaultAgentVersion("code-refactor", "2.0.0");
```

Agents that ship multiple `name@version` registrations are advertised on `session.welcome.capabilities.agents` (spec §6.2, §7.5).

## Accept a session

```csharp
await server.AcceptAsync(transport, cancellationToken);
```

For ASP.NET Core hosting, use `MapArcp("/arcp")` from `Arcp.AspNetCore`. The endpoint adapter handles WebSocket upgrade, allowed-hosts checks, and lifecycle wiring.

## What the runtime does

Per session, the runtime:

1. Authenticates the bearer token (spec §6.1).
2. Sends `session.welcome` with feature flags, agent inventory, resume token, heartbeat interval (spec §6.2).
3. Dispatches inbound envelopes (`job.submit`, `job.cancel`, `session.ack`, `session.list_jobs`, `job.subscribe`).
4. Runs the heartbeat watchdog (spec §6.4) and back-pressure detector (spec §6.5).
5. Per `job.submit`, runs the agent in a linked cancellation scope tied to the session and (optionally) a `lease_expires_at` watchdog (spec §9.5).
