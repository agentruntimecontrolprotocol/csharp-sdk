# Arcp.Runtime

`Arcp.Runtime` provides `ArcpServer` — the runtime side of the protocol. It
accepts sessions, authenticates callers, dispatches job submissions to
registered agents, and manages the full job lifecycle.

```sh
dotnet add package Arcp.Runtime
```

> **Tip:** most apps reference the `Arcp` meta-package instead, which
> re-exports `Arcp.Runtime` along with `Arcp.Core` and `Arcp.Client`.

## Configure

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
    Auth    = new StaticBearerVerifier(
        ("tok-alice", new AuthPrincipal("alice@example.com"))
    ),
    HeartbeatIntervalSec    = 30,
    BackPressureThreshold   = 1000,
});
```

### ArcpServerOptions

| Property                | Default                  | Purpose                                              |
| ----------------------- | ------------------------ | ---------------------------------------------------- |
| `Runtime`               | required                 | Name / version sent in `session.welcome`.            |
| `Auth`                  | `null` (rejects all)     | Token verification (see [Auth](../guides/auth.md)).  |
| `Features`              | all v1.1 features        | Advertised feature flags (spec §6.2).                |
| `Encodings`             | `["json"]`               | Advertised envelope encodings.                       |
| `HeartbeatIntervalSec`  | `30`                     | Advertised ping interval (spec §6.4).                |
| `ResumeWindowSec`       | `600`                    | Replay buffer window (spec §6.3).                    |
| `BackPressureThreshold` | `1000`                   | Unacked event count before back-pressure signals.    |
| `AuthorizationPolicy`   | `SamePrincipalPolicy`    | Decides who can observe/cancel jobs (spec §6.6).     |
| `CredentialProvisioner` | `null`                   | Issues short-lived credentials after lease sign-off. |
| `CredentialStore`       | `InMemoryCredentialStore`| Durable store for revocation recovery on restart.    |
| `TimeProvider`          | `TimeProvider.System`    | Clock injection for tests.                           |

## Register agents

```csharp
// Versionless — accessed as "echo" or "echo@latest":
server.RegisterAgent("echo", async (ctx, ct) => ctx.Input);

// With explicit versions:
server.RegisterAgentVersion("code-refactor", "1.0.0", new MyCodeRefactorV1());
server.RegisterAgentVersion("code-refactor", "2.0.0", new MyCodeRefactorV2());
server.SetDefaultAgentVersion("code-refactor", "2.0.0");
```

Registered agents and their versions are advertised in
`session.welcome.capabilities.agents` (spec §6.2, §7.5).

## Agent handler signature

```csharp
Func<JobContext, CancellationToken, Task<object?>> handler
```

`JobContext` exposes:

| Member                                                   | Description                                                |
| -------------------------------------------------------- | ---------------------------------------------------------- |
| `ctx.JobId` / `ctx.SessionId` / `ctx.Agent`              | Identifiers for this dispatch.                             |
| `ctx.Input`                                              | Deserialized job input (`JsonElement?`).                   |
| `ctx.Lease` / `ctx.LeaseConstraints` / `ctx.Budget`      | Finalized lease and remaining cost budget.                 |
| `ctx.TraceId`                                            | W3C trace ID propagated from the client.                   |
| `ctx.Credentials`                                        | Provisioned credentials (value stripped).                  |
| `ctx.Cancellation` / `ctx.Logger`                        | Standard cancellation + `ILogger` for the agent.           |
| `ctx.LogAsync(level, message, ct)`                       | Emit a `log` event to the client.                          |
| `ctx.ThoughtAsync(text, ct)`                             | Emit a `thought` event.                                    |
| `ctx.StatusAsync(phase, message, ct)`                    | Emit a `status` event.                                     |
| `ctx.ProgressAsync(current, total, units, message, ct)`  | Emit a `progress` event (spec §8.2.1).                     |
| `ctx.ToolCallAsync(tool, callId, args, ct)`              | Emit a `tool_call` event.                                  |
| `ctx.ToolResultAsync(callId, result, error, ct)`         | Emit a `tool_result` event.                                |
| `ctx.MetricAsync(name, value, unit, dimensions, ct)`     | Emit a `metric` event (debits `cost.budget` when matched). |
| `ctx.ArtifactRefAsync(uri, contentType, byteSize, sha256, ct)` | Emit an `artifact_ref` event.                        |
| `ctx.DelegateAsync(childJobId, agent, input, ct)`        | Record a delegation in the job event stream.               |
| `ctx.BeginResultStream()`                                | Allocate a `ResultId` for a streamed result (spec §8.4).   |
| `ctx.WriteChunkAsync(resultId, text \| bytes, more, ct)` | Emit one `result_chunk`.                                   |
| `ctx.EmitEventAsync(kind, body, ct)`                     | Emit any event kind (including `x-vendor.*`).              |
| `ctx.RotateCredentialAsync(id, replacement, ct)`         | Rotate a provisioned credential.                           |

The authenticated principal is held on the session, not on
`JobContext`. Authorization decisions (who can observe or cancel a job)
go through `ArcpServerOptions.AuthorizationPolicy`.

## Accept a session

```csharp
// Transport-agnostic (tests, stdio):
await server.AcceptAsync(transport, cancellationToken);

// ASP.NET Core — use MapArcp instead (Arcp.AspNetCore):
app.MapArcp(server, o => { o.Path = "/arcp"; });
```

## What the runtime does per session

1. Authenticates the bearer token (spec §6.1).
2. Sends `session.welcome` with feature flags, agent inventory, resume token,
   and heartbeat interval (spec §6.2).
3. Dispatches inbound envelopes:
   - `job.submit` → validate lease, allocate job, run agent handler.
   - `job.cancel` → signal `CancellationToken` for that job.
   - `session.ack` → advance back-pressure window.
   - `session.list_jobs` → return filtered job list with cursor.
   - `job.subscribe` → add session to fan-out set for a job.
4. Runs the heartbeat watchdog (spec §6.4) — cancels jobs on `HEARTBEAT_LOST`.
5. Per `job.submit`, runs the agent in a linked cancellation scope tied to the
   session and optionally a `lease_expires_at` watchdog (spec §9.5).

## Error handling

Agents that throw an `ArcpException` subclass preserve the `Code` on the wire.
Any other unhandled exception becomes `INTERNAL_ERROR`. See
[Errors guide](../guides/errors.md).

## Related

- [Auth guide](../guides/auth.md) — `IBearerVerifier`, `StaticBearerVerifier`.
- [Leases guide](../guides/leases.md) — `LeaseManager`, `AssertSubset`.
- [Delegation guide](../guides/delegation.md) — `ctx.DelegateAsync`.
- [Arcp.AspNetCore](./Arcp.AspNetCore.md) — Kestrel hosting.
- [Arcp.Hosting](./Arcp.Hosting.md) — DI registration helper.
