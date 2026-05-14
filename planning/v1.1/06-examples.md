# 06 — Examples

Maps the 23 TypeScript examples (`../typescript-sdk/examples/README.md`)
to C# sample projects under `samples/`. The current SDK ships 14
sample projects predating v1.0 alignment (`02-current-audit.md` §6);
all 14 are deleted, renamed, or rewritten in lockstep with the wire
re-keying (audit §1, §6).

## 0. The "18 vs 23" reconciliation

`BOOTSTRAP.md` Phase 6 says "18 TS examples". The TS `examples/README.md`
ships **23**: 9 v1.0 core + 9 v1.1 features + 4 host integrations + 1
`stdio` already counted in v1.0 core (so 9 + 9 + 4 = 22, plus `stdio`
double-counts depending on how you read the spec). The "18" in
bootstrap most plausibly meant **9 v1.0 + 9 v1.1 = 18 feature
examples**, treating the 4 host integrations as middleware-package
samples (Phase 5 territory, not Phase 6). I am taking the wider
reading: **all 23** map to C# samples, because the host integrations
have direct C# analogues — `tracing/` → `Arcp.Otel` driver,
`express/`+`fastify/`+`bun/` → one `AspNetCore/` sample covering
ASP.NET Core minimal-API hosting (Phase 5 `Arcp.AspNetCore`). I
collapse the three TS HTTP-host examples into **one** C# sample
(`AspNetCore/`) because they demonstrate the same C# code shape under
different runtimes — there is no `Bun`-vs-`Fastify`-vs-`Express`
distinction on .NET. Net C# sample count: **21** (9 + 9 + 1 collapse
of 3 + 1 OTel + 1 stdio already in core = 21).

## 1. Project shape and split-process choice

Each sample is **one `.csproj`** (`<OutputType>Exe</OutputType>`,
`net10.0`, references the `Arcp` umbrella) with two files —
`Program.Server.cs` and `Program.Client.cs` — selected by **launch
profile** (`Properties/launchSettings.json` declares `server` and
`client` profiles). Run as:

```sh
dotnet run --project samples/SubmitAndStream --launch-profile server
dotnet run --project samples/SubmitAndStream --launch-profile client
```

Rationale for **single `.csproj` + launch profiles** over two sibling
`.csproj`s:

- Two projects per sample doubles `dotnet build` graph nodes (21
  samples → 42 projects in `ARCP.sln`); one `.csproj` keeps the
  solution at 21 sample entries.
- `Program.Server.cs` and `Program.Client.cs` share the agent type
  declaration and option-parsing helpers without an extra
  `Sample.Shared.csproj`.
- The selector is `args[0]` (`server` or `client`) wired through
  `commandLineArgs` in the launch profile — no `#if SERVER` /
  `#if CLIENT` build flags, no `Microsoft.NET.Sdk.Web` separation.
- The `Stdio/` example is the one exception: the client *spawns* the
  server. Same `.csproj`; the launch profile defaults to `client`
  and the client uses `System.Diagnostics.Process.Start("dotnet",
  "run --project . --launch-profile server")` so a single
  `dotnet run --project samples/Stdio` runs both halves.

`Program.cs` is one entry point that dispatches:

```csharp
return args is ["server", ..] ? await Program.Server.RunAsync(args[1..])
     : args is ["client", ..] ? await Program.Client.RunAsync(args[1..])
     : 2;
```

## 2. Common harness: `samples/_shared/`

`samples/_shared/_Shared.csproj` is a small **internal** library
(`<OutputType>Library</OutputType>`, not in any NuGet pack, not
referenced by `src/`) containing `SampleHarness` with three static
helpers:

| Member                                          | Purpose                                                                                       |
| ----------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `SampleHarness.ConfigureLogging(LogLevel)`      | `ILoggerFactory` with a one-line console formatter (no `Microsoft.Extensions.Hosting` pull)   |
| `SampleHarness.LoadOptions()`                   | Reads `ARCP_DEMO_PORT`, `ARCP_DEMO_URL`, `ARCP_DEMO_TOKEN`; returns `SampleOptions` record    |
| `SampleHarness.WaitForReadyAsync(uri, CT)`      | Polls `http://host:port/healthz` (or TCP connect for stdio) until 200; 5 s budget, exits 75   |

**Worth it vs. copy-paste:** worth it — the alternative is the same
12-line `Environment.GetEnvironmentVariable` block 21 times. The
project is `internal` to the samples; deleting it costs nothing.
Build cost is one extra reference per sample (`<ProjectReference
Include="..\_shared\_Shared.csproj" />`), negligible.

`SampleOptions` is a `readonly record struct`:

```csharp
public readonly record struct SampleOptions(int Port, Uri Url, string Token);
```

Defaults come from a per-sample constant when env vars are unset.

## 3. Port allocation (5500–5523)

One port per sample so the whole suite can run in parallel (mirrors
TS convention). Unused range slots remain for future v1.1+ samples.

| Port | Sample                |
| ---- | --------------------- |
| 5500 | `SubmitAndStream`     |
| 5501 | `Delegate`            |
| 5502 | `Resume`              |
| 5503 | `IdempotentRetry`     |
| 5504 | `LeaseViolation`      |
| 5505 | `Cancel`              |
| 5506 | `Stdio` (unused — process pipes) |
| 5507 | `VendorExtensions`    |
| 5508 | `CustomAuth`          |
| 5509 | `Heartbeat`           |
| 5510 | `AckBackpressure`     |
| 5511 | `ListJobs`            |
| 5512 | `Subscribe`           |
| 5513 | `AgentVersions`       |
| 5514 | `LeaseExpiresAt`      |
| 5515 | `CostBudget`          |
| 5516 | `Progress`            |
| 5517 | `ResultChunk`         |
| 5518 | `Tracing`             |
| 5519 | `AspNetCore`          |
| 5520–5523 | reserved          |

## 4. TS → C# mapping

`Idiom` names a specific C# language or BCL feature the sample
exercises. `Spec` cites the v1.1 draft section per
`01-spec-delta.md`. `Action` records whether the target directory
replaces an existing `samples/X/` (`02-current-audit.md` §6).

### v1.0 core

| TS dir                  | C# sample project          | Files                                              | Spec              | Idiom                                                                                                                                                          | Action                       |
| ----------------------- | -------------------------- | -------------------------------------------------- | ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- |
| `submit-and-stream/`    | `samples/SubmitAndStream/` | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §13.1, §7.1, §8.2 | `await foreach (var ev in handle.Events.WithCancellation(ct))` on `IAsyncEnumerable<JobEvent>`; pattern match `ev` against `JobEvent.Log`/`Status`/`ToolCall` records | New                          |
| `delegate/`             | `samples/Delegate/`        | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §13.2, §10        | Parent `IAgent.RunAsync` calls `ctx.DelegateAsync(AgentRef.Parse("child"), input, ct)`; child `JobHandle.TraceId` equals parent's `Guid` (UUIDv7)               | Replaces `samples/Delegation/` |
| `resume/`               | `samples/Resume/`          | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §13.3, §6.3       | `ArcpClient.ResumeAsync(sessionId, lastEventSeq, ct)`; `await using` on the connection so the disconnect step is `await connection.DisposeAsync()`             | Replaces `samples/Resumability/` |
| `idempotent-retry/`     | `samples/IdempotentRetry/` | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §13.5, §7.2       | `IdempotencyKey` `readonly record struct` implementing `IParsable<IdempotencyKey>`; identical `(principal, key)` returns same `JobId`                          | New                          |
| `lease-violation/`      | `samples/LeaseViolation/`  | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §13.4, §9.3       | `JobEvent.ToolResult` carries `ErrorCode.PermissionDenied`; client switches on the discriminated union, job still completes                                    | Replaces `samples/Leases/` (rewrite contents) |
| `cancel/`               | `samples/Cancel/`          | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §7.4              | `CancellationToken` propagated through `JobHandle.CancelAsync(ct)`; server-side `IAgent.RunAsync(ctx, ct)` observes `ct` and returns; terminal envelope is `JobError { FinalStatus: "cancelled" }` | Replaces `samples/Cancellation/` |
| `stdio/`                | `samples/Stdio/`           | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §4.2, §22         | `System.Diagnostics.Process.Start` with `RedirectStandardInput/Output = true`; `StdioTransport` reads `Stream` line-delimited UTF-8 via `PipeReader`            | New                          |
| `vendor-extensions/`    | `samples/VendorExtensions/`| `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §8.2, §9.2, §15   | `JsonExtensionData` dictionary on `JobEvent` captures `x-vendor.acme.progress`; naïve handler iterates known kinds, vendor-aware handler reads `event.Extensions["x-vendor.acme.progress"]` via `JsonElement` | Replaces `samples/Extensions/` |
| `custom-auth/`          | `samples/CustomAuth/`      | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §6.1              | `IBearerVerifier` (DI-free interface); HMAC `IncrementalHash` over claims; bad token → `ArcpException(ErrorCode.Unauthenticated)` thrown at handshake          | New                          |

### v1.1 features

| TS dir              | C# sample project        | Files                                              | Spec       | Idiom                                                                                                                                                       | Action                              |
| ------------------- | ------------------------ | -------------------------------------------------- | ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------- |
| `heartbeat/`        | `samples/Heartbeat/`     | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §6.4       | `PeriodicTimer` on `SessionWelcome.HeartbeatIntervalSec`; `TimeProvider` injected so tests fast-forward — sample shows the production wall-clock variant    | Replaces `samples/Heartbeats/` (rename + rewrite) |
| `ack-backpressure/` | `samples/AckBackpressure/` | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §6.5, §8.2 | `Channel<Envelope>` bounded with `BoundedChannelFullMode.Wait`; client emits `session.ack` every N events; server emits `status { phase: "back_pressure" }` when `WriteAsync` blocks | New                                 |
| `list-jobs/`        | `samples/ListJobs/`      | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §6.6       | `ArcpClient.ListJobsAsync(filter, ct)` returns `JobListPage` with `NextCursor`; client drives pagination with a `while (cursor is not null)` loop          | New                                 |
| `subscribe/`        | `samples/Subscribe/`     | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §7.6, §6.6 | Second `ArcpClient` calls `SubscribeAsync(jobId, history: true)`; consumed via `await foreach (var ev in sub.Events.WithCancellation(ct))`; cross-session `CancelAsync` throws `ArcpException(ErrorCode.PermissionDenied)` | Replaces `samples/Subscriptions/` (rename + rewrite) |
| `agent-versions/`   | `samples/AgentVersions/` | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §7.5, §12  | `AgentRef` `readonly record struct` implementing `IParsable<AgentRef>` — `AgentRef.Parse("echo@2.0")`; unknown version throws `AgentVersionNotAvailableException` | New                                 |
| `lease-expires-at/` | `samples/LeaseExpiresAt/`| `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §9.5, §12  | `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` advanced past `expires_at`; agent's `ctx.ValidateLeaseOp` trips, runtime watchdog (`PeriodicTimer` on `TimeProvider`) emits `LEASE_EXPIRED` | New                                 |
| `cost-budget/`      | `samples/CostBudget/`    | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §9.6, §12  | `BudgetAmount` parses `"USD:1.50"` to `(decimal Amount, string Currency)`; `Dictionary<string, decimal>` ledger; final `metric{ unit: "USD" }` trips `BudgetExhaustedException` | New                                 |
| `progress/`         | `samples/Progress/`      | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §8.2.1     | `IProgress<ProgressBody>` adapter — client constructs `Progress<ProgressBody>(body => RenderBar(body.Current, body.Total))` and passes through `JobHandle.ReportProgressTo(progress, ct)` | New                                 |
| `result-chunk/`     | `samples/ResultChunk/`   | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §8.4       | Server `ctx.StreamResultAsync(chunks, ct)` over `IAsyncEnumerable<ResultChunkBody>`; client `await foreach (var chunk in handle.Chunks(ct))` reassembles via `ArrayPool<byte>.Shared` rented buffers | New                                 |

### Host integrations

| TS dir       | C# sample project     | Files                                              | Spec | Idiom                                                                                                                                                                       | Action |
| ------------ | --------------------- | -------------------------------------------------- | ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| `tracing/`   | `samples/Tracing/`    | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §11  | `System.Diagnostics.ActivitySource` + `Arcp.Otel` middleware adds `traceparent` to `extensions["x-vendor.opentelemetry.tracecontext"]`; spans printed via `ConsoleExporter` (OpenTelemetry.Exporter.Console) from `OpenTelemetrySdk` configured in the *sample*, not the SDK | New    |
| `express/` + `fastify/` + `bun/` (3 TS examples) | `samples/AspNetCore/` | `Program.cs`, `Program.Server.cs`, `Program.Client.cs` | §4.1 | `WebApplication.CreateBuilder` minimal-API; `app.UseWebSockets()`; `app.MapArcp("/arcp", opts => opts.AllowedHosts = ["localhost"])` from `Arcp.AspNetCore`; alongside `app.MapGet("/", ...)` to prove the same Kestrel port serves both | New    |

## 5. Existing v0 samples — what happens to each

| Current `samples/X/`      | Action                                       | Why                                                                                                                                                          |
| ------------------------- | -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Cancellation/`           | Rename → `samples/Cancel/`, replace contents | TS canonical name is `cancel/`; contents target the v0 envelope `cancel` type (`02-current-audit.md` §1 row §7.4)                                            |
| `CapabilityNegotiation/`  | **Delete**                                   | v1.1 capability negotiation is exercised implicitly by every sample on connect (§6.2 intersection); no dedicated example in TS, none here either              |
| `Delegation/`             | Rename → `samples/Delegate/`, replace contents | TS canonical name is `delegate/`; current sample targets `agent.delegate` envelope type which v1.1 deletes (audit §6)                                        |
| `Extensions/`             | Rename → `samples/VendorExtensions/`, replace contents | TS canonical name is `vendor-extensions/` (§15); current sample structure predates the `x-vendor.*` namespace convention                                  |
| `Handoff/`                | **Delete**                                   | `agent.handoff` envelope is not a v1.0 or v1.1 feature (audit §6); no TS analogue                                                                            |
| `Heartbeats/`             | Rename → `samples/Heartbeat/`, replace contents | TS canonical name is singular `heartbeat/`; current sample uses v0 `ping`/`pong` envelopes (audit §4 row §6.4) which v1.1 replaces with `session.ping`/`pong` |
| `HumanInput/`             | **Delete**                                   | Not a v1.0 or v1.1 feature; no TS analogue                                                                                                                   |
| `LeaseRevocation/`        | **Delete**                                   | v1.0/v1.1 have no `lease.revoked` envelope; lease lifecycle is "granted at submit, expires at `expires_at`" (audit §6)                                       |
| `Leases/`                 | Rename → `samples/LeaseViolation/`, replace contents | TS canonical name; current sample assumes v0 `lease.granted`/`lease.refresh` envelopes which v1.1 removes                                                |
| `MCP/`                    | **Delete**                                   | Not a v1.0 or v1.1 feature; belongs in a separate adapter project, out of scope                                                                              |
| `PermissionChallenge/`    | **Delete**                                   | Not a v1.0 or v1.1 feature; v1.0 auth is bearer-only at handshake (§6.1), no challenge flow                                                                  |
| `ReasoningStreams/`       | **Delete**                                   | v1.1 explicitly defers "streaming-token surface for LLM outputs" (`01-spec-delta.md` §16); result streaming is `result_chunk` (`samples/ResultChunk/`)        |
| `Resumability/`           | Rename → `samples/Resume/`, replace contents | TS canonical name is `resume/`                                                                                                                               |
| `Subscriptions/`          | Rename → `samples/Subscribe/`, replace contents | TS canonical name is singular `subscribe/`; current sample uses v0 `subscribe`/`subscribe.event` envelopes which v1.1 replaces with `job.subscribe`/`job.subscribed`/`job.unsubscribe` (§7.6) |

Net: 6 deletes, 7 renames + content rewrites, 14 new projects added
(13 v1.0/v1.1 + 1 `AspNetCore/`).

## 6. Runner contract

Every sample exits 0 on assertion success. Conventions:

- **Failure exits:** `1` for assertion failure, `2` for usage error
  (bad args), `75` (`EX_TEMPFAIL`) for `WaitForReadyAsync` timeout.
- **Stdout:** human-readable demonstration narrative only; no JSON
  dumps. Logger writes to `stderr`.
- **No external network:** `ARCP_DEMO_URL` defaults to
  `ws://127.0.0.1:<port>/arcp` per sample.
- **No `dotnet test`:** these are `Exe`, not xUnit. Conformance tests
  live in `tests/` (Phase 7).
- **One-command for `Stdio/`:** `dotnet run --project samples/Stdio`
  with no profile; the dispatcher in `Program.cs` defaults to `client`
  when `args` is empty, and the client spawns `dotnet run --project .
  --launch-profile server` as a child via `Process.Start`.

The CI step that proves the suite holds together is a small shell
script (`scripts/run-samples.sh`) that, per sample, launches the
server background, awaits ready, runs the client, asserts exit 0,
kills the server. Not Phase 6 scope — Phase 7 tests own the
assertion shape — but the sample directories MUST be structured to
make this script trivial: each one declares its port via a constant
`SamplePort` in `Program.cs`.

## 7. What every sample's `Program.cs` looks like (~15 LOC)

```csharp
using Arcp.Samples.Shared;

return args switch
{
    ["server", .. var rest] => await Program.Server.RunAsync(rest, default),
    ["client", .. var rest] => await Program.Client.RunAsync(rest, default),
    []                      => await Program.Client.RunAsync([], default),  // stdio default
    _                       => 2,
};
```

`Program.Server` and `Program.Client` are `static partial class
Program { public static class Server { ... } }` inside the
single-file-top-level program (or two separate files declaring
`partial class Program`). This keeps the C# idiom shown off in
the table column (e.g. `await foreach` over `IAsyncEnumerable`)
unobstructed by ceremony.

## 8. Open follow-ups for Phase 10

- The `AspNetCore/` collapse from three TS examples loses the
  per-runtime quirks (pino logger in fastify, Bun's WS impl). If
  Phase 5 ships separate `Arcp.AspNetCore.Logging` integration, the
  sample can split, but not in v1.1 scope.
- `Tracing/` requires the consumer to pull
  `OpenTelemetry.Exporter.Console` for the demo print — fine for a
  sample, not fine in `src/Arcp.Otel`. The sample's `.csproj` adds
  the package ref directly; `src/Arcp.Otel` stays
  `System.Diagnostics.DiagnosticSource`-only.
- `Stdio/`'s `Process.Start` of `dotnet run` requires the SDK to be
  on `PATH` at sample run time; document this in the sample's
  `Program.Client.cs` comment header, not in a README.
