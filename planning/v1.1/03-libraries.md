# 03 — Library & Package Picks for v1.1

Targets: `net9.0` (LTS) primary; `net10.0` once GA lands and `global.json`
moves off the `10.0.203` preview pin. CPM is on
(`Directory.Packages.props`, `CentralPackageTransitivePinningEnabled=true`)
so every transitive is fixed at the project root — adding a package is
not free.

## Why these picks

- BCL covers transport, IDs, async, streams, tracing, and JSON; the
  only non-Microsoft library the runtime ships is `Cysharp/Ulid` for
  §5.1 envelope alternates. Every other rejected alternative is
  named below.
- AOT survives because `System.Text.Json` source-gen
  (`[JsonSerializable]` + `JsonSerializerContext`) replaces reflection;
  no `BinaryFormatter`, no reflection-based DI, no `Newtonsoft.Json`.
- The library takes `ILogger` from
  `Microsoft.Extensions.Logging.Abstractions` and an `ActivitySource`
  from `System.Diagnostics.DiagnosticSource`; no OTel SDK, no logging
  provider, no DI container ships in `Arcp.Core` —
  `Arcp.AspNetCore` and `Arcp.Otel` (Phase 5) wire those.

## Per-concern picks

### JSON — `System.Text.Json` 9.0.x (BCL)

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `System.Text.Json` (source-gen `JsonSerializerContext`) | 9.0.x (BCL) | Over `Newtonsoft.Json 13.x`: `[JsonPolymorphic]` + `[JsonDerivedType]` lands the §5.1 `type` discriminator without a custom converter per message, source-gen is `PublishAot`-safe, and `JsonUnknownTypeHandling` plus extension-data `JsonElement` carry the §5.1 "unknown top-level fields MUST be ignored" rule on round-trip — `Newtonsoft` has none of these properties under AOT. | spec §5.1; current `Envelope/EnvelopeJsonConverter.cs` (audit §1) |

The custom converter at `Envelope/EnvelopeJsonConverter.cs` stays
(audit §5 keep-list) but the `MessageTypeRegistry.CoreCatalog()`
swap (audit §1) is the chance to fold the dispatch into a
`[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` base
plus `[JsonDerivedType]` per spec message — and keep the converter
only for the `welcome.capabilities.agents` rich-union shape
(`string[]` ∨ `{name, versions[], default?}[]`) noted in audit §4
where `JsonElement.ValueKind` probing is required.

`JsonSchema.Net 7.2.3` (pinned) is only used in tests / conformance
fixtures; it never lives on the hot path. Hold it at 7.2.3 unless a
test needs newer.

### WebSocket client — BCL `ClientWebSocket` + manual 101 upgrade

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `System.Net.WebSockets.ClientWebSocket` (BCL) + `HttpClient` (BCL) for the upgrade handshake | 9.0.x (BCL) | Over `Websocket.Client` (Marfusios): the wire-level concern is reading the v1.1 capability headers off the 101 response (`Sec-WebSocket-Protocol`, X-prefixed feature headers), and `ClientWebSocket.ConnectAsync` does not expose `HttpResponseMessage` for the upgrade — audit §4 §6.2 calls this out. Workaround: build the `GET /arcp` with `Upgrade: websocket` via `HttpClient.SendAsync`, read response headers off the returned `HttpResponseMessage`, then hand the socket from `HttpResponseMessage.Content.ReadAsStreamAsync()` to `WebSocket.CreateFromStream(...)`. `Websocket.Client` adds a dependency for what the BCL already does — and still doesn't surface the upgrade response. | spec §4.1, §6.2; audit §4 row §6.2 |

This is the single C# pitfall the audit names; pin it here so Phase 4
doesn't relearn it.

### WebSocket server — ASP.NET Core WebSocketMiddleware

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `Microsoft.AspNetCore.App` (shared framework) — `app.UseWebSockets()` + `MapGet("/arcp", ...)` accept-on-upgrade | 9.0.x | Over `SuperSocket.WebSocket.Server 2.x`: Kestrel + `WebSocketMiddleware` already speak RFC 6455 inside Microsoft's framework dependency; deployers don't add a runtime, `IEndpointRouteBuilder.MapArcp("/arcp")` from `Arcp.AspNetCore` (Phase 5) plugs straight in, and `HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { SubProtocol = "arcp.v1" })` is the point where v1.1 capability headers are emitted on the server side. | spec §4.1; Phase 5 plan |

`Microsoft.AspNetCore.Http.Connections` is implicitly part of the
shared framework — no extra `PackageReference` needed for the
WebSocket path; only the SignalR connection multiplexer pulls a
separate reference, and we don't use SignalR.

### HTTP — BCL `HttpClient`; `IHttpClientFactory` in middleware only

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `System.Net.Http.HttpClient` (BCL) | 9.0.x (BCL) | Over `Flurl.Http 4.x` or `RestSharp 112.x`: `HttpClient` is what's needed for the §6.2 upgrade handshake above and nothing else in ARCP is RESTful — the wire is WebSocket / stdio. `Arcp.Core` stays factory-agnostic (takes an `HttpMessageHandler` or `HttpClient`); `Arcp.AspNetCore` adds `IHttpClientFactory` registration so consumers get DNS-refresh and socket-handler pooling for free. | spec §4; audit §5 |

### Async, streams, channels — all BCL

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `Task` / `ValueTask` / `IAsyncEnumerable<T>` (BCL) + `System.Threading.Channels` | 9.0.x (BCL) | Over `System.Reactive 6.x`: `IAsyncEnumerable<ResultChunk>` is the §8.4 client surface (audit §4 row §8.4), and `Channel<Envelope>` with `BoundedChannelOptions { FullMode = Wait, Capacity = N }` is the §6.5 ack/back-pressure boundary (audit §4 row §6.5). `Rx.NET` would force `IObservable<T>` on the public surface — non-idiomatic in modern C# and incompatible with `await foreach`. | spec §6.5, §8.4 |

### Logging — abstractions only

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `Microsoft.Extensions.Logging.Abstractions` | 9.0.x (refresh from 9.0.0) | Over `Serilog 4.x` or `NLog 5.x`: the library accepts `ILogger<T>` from the abstractions package and ships no provider — `Serilog` / `NLog` would force a provider transitively into every consumer. `LoggerMessage.Define` source-gen (BCL since net6) gives zero-alloc structured logs without a third-party. | BOOTSTRAP §3 ground rule |

`Microsoft.Extensions.Logging.Console 9.0.0` stays a **test-only**
reference (already in `Directory.Packages.props` under "Test").

### ID generation — Cysharp `Ulid` + BCL `Guid.CreateVersion7`

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `Ulid` (Cysharp) | 1.3.4 → refresh to 1.3.4 (current); track Cysharp/Ulid releases | Over `NUlid 1.7.x`: Cysharp `Ulid` is a `readonly struct` with `Span<byte>`-based parse/format and `IUtf8SpanFormattable` — zero alloc on the hot envelope path. `NUlid` allocates a backing `byte[]`. Cysharp is already pinned (audit §2). | audit §5 keep-list; spec §5.1 envelope `id` |
| `Guid.CreateVersion7()` (BCL net9+) | BCL | UUIDv7 is required for §5.1 envelope `id` per the spec wire format; the BCL got `Guid.CreateVersion7()` in .NET 9, so no NuGet dependency is needed. Over `Medo.Uuid7` and similar third-party UUIDv7 packages: the BCL covers it; an extra package is unjustified once `net9.0` is the floor TFM. | spec §5.1; audit §5 keep-list |

Keep both: ULID is the existing wrapper surface in `Ids/*`; UUIDv7 is
the wire requirement. The wrapper exposes both as `IdFactory`
methods so call sites don't pick.

### Tracing — `ActivitySource` only in `Arcp.Core`

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `System.Diagnostics.DiagnosticSource` `ActivitySource` (BCL) | 9.0.x (BCL) | Over `OpenTelemetry.Api` in `Arcp.Core`: the BCL `ActivitySource` is the OTel-compatible canonical API; OTel SDK pulls exporters and protocol buffers that consumers pay for whether or not they export. The Phase 5 `Arcp.Otel` adapter is the one place `OpenTelemetry.Api` becomes a `PackageReference` — no exporter pins (`OpenTelemetry.Exporter.OpenTelemetryProtocol`, `.Console`, `.Zipkin` are consumer choices, not ours). | BOOTSTRAP §3 hard rule; spec §11 |

`Arcp.Otel` adds `OpenTelemetry.Api 1.9.x` only — adapter-layer
ergonomics (`AddArcpInstrumentation()` extension on
`TracerProviderBuilder`) without bundling exporters.

### Testing stack

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `xunit` + `xunit.runner.visualstudio` | stay on 2.9.2 / 2.8.2 (refresh to xunit 2.9.3 + runner 2.8.3) | Over `xunit.v3 1.x` (released 2025): xunit v3 brings a worthwhile assembly-per-test-class isolation model but `Microsoft.NET.Test.Sdk 17.11.1` and `coverlet.collector 6.0.2` integration with v3 is still settling. Holding on v2 for the v1.1 PR; revisit when xunit.v3 has parity with the VS test runner. Over `NUnit 4.x` / `MSTest 3.x`: xunit is already the harness; switching cost is unjustified. | audit §2 / §7 |
| `FluentAssertions` | 6.12.2 → **pin at `[6.12.2,7.0.0)`** (do not move to 8.x) | Over `FluentAssertions 8.x`: the 8.0 release changed to a commercial license. Stay on v6/v7 (Apache-2.0). Evaluate `Shouldly 4.x` (BSD-2-Clause) as the swap for the next major: `Shouldly` covers the common assertion surface without the license attached to `FluentAssertions 8`. Decision: keep v6 for v1.1; track `Shouldly` for v1.2. | License-driven — see notes below |
| Fakes (hand-rolled) | n/a | Over `NSubstitute 5.x` / `Moq 4.x` for FSM tests: BOOTSTRAP §3 prefers fakes for state machines because mocks invert the assertion — you assert on the call shape instead of the resulting state. NSubstitute stays available for non-FSM seams (e.g. `IJobAuthorizationPolicy` permissions stub) but is not the default. | BOOTSTRAP §3 |
| `Verify.Xunit` | 26.x (current major) | Over hand-rolled JSON-string compares for envelope round-trip snapshots: `Verify` writes a `.received.txt` next to `.verified.txt` and fails on diff — the §5.1 envelope snapshot test is exactly this shape. Over `Snapshooter`: `Verify` is the de facto choice for STJ output and has better diff-tool integration. | spec §5.1 |
| `BenchmarkDotNet` | 0.14.x | For envelope encode/decode and `Channel<Envelope>` enqueue/dequeue benchmarks (§6.5 back-pressure characterization). Test-only; never a library `PackageReference`. | n/a |
| `FsCheck.Xunit` (property tests) | 3.x | Argument **for**: §5.1 unknown-field passthrough and §9.4 lease subsetting are total-order invariants where property tests cover the input space better than examples. Argument **against**: shrinking C# records is awkward and the team has no current FsCheck habit. Decision: **include but scope to two specs** — envelope round-trip (unknown fields) and `LeaseManager.AssertSubset` — not as a general harness. | spec §5.1, §9.4 |

### Coverage + mutation

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `coverlet.collector` + `ReportGenerator` | coverlet 6.0.2 (stay); `dotnet-reportgenerator-globaltool` 5.4.x | Over `dotCover` (JetBrains, paid) / `OpenCover` (unmaintained): coverlet is already pinned, integrates with `dotnet test` via `--collect "XPlat Code Coverage"`, and `reportgenerator` produces the merged HTML the CI badge consumes. | audit §2 / §7 |
| `Stryker.NET` | 4.x (`dotnet-stryker`) | **Yes, nightly, budgeted.** Mutation testing finds equality-flip and boundary-off-by-one bugs that line coverage misses — in particular, §9.5 `expires_at` comparison and §9.6 budget-counter sign checks are exactly the class of mutant Stryker catches. Budget the run: scope `<ProjectFilter>` to `src/Arcp.Core/` only, `<ThresholdHigh>` 70, `<ThresholdLow>` 60, `<Mutate>` excludes `Generated/*.g.cs` and `Program.cs`. The full unit-test pass under mutants is expensive; nightly CI (not PR) is the cadence. | spec §9.5, §9.6 |

### Lint / analyzers — pick a four-package stack

The repo already runs with `AnalysisLevel = latest-recommended` +
`AnalysisMode = All` + `TreatWarningsAsErrors = true`
(`Directory.Build.props`). That implicitly includes
`Microsoft.CodeAnalysis.NetAnalyzers` from the SDK. Add three more
analyzer packages; drop none.

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `Microsoft.CodeAnalysis.NetAnalyzers` | implicit (SDK 10.x) | The CA-prefix rules. Already on via `AnalysisMode=All` — confirm no `<EnableNETAnalyzers>false` regression. | `Directory.Build.props` line 11 |
| `StyleCop.Analyzers` | 1.2.0-beta.556 → **plan migration off beta** | Stay for v1.1; the SC-prefix rules are house style. `1.2.0-beta.556` is the only published 1.2 build and there has been no stable release — track the StyleCopAnalyzers repo for a stable 1.2.0 / 2.0.0 and pin off the beta when it lands. Over `Roslynator.Formatting.Analyzers` formatting rules: StyleCop is configured (the team has `stylecop.json` / `.editorconfig` muscle memory). | audit §3 |
| `Roslynator.Analyzers` | 4.13.x | RCS-prefix simplifications (collection initializers, async patterns, `ConfigureAwait` flagging). Catches the modern-C# rewrites the audit calls out (`record struct AgentRef`, `IAsyncEnumerable<T>` shapes). Over `SonarAnalyzer.CSharp` (Sonar, broader rules, slower CI): Roslynator is faster and more focused for an SDK. | audit §3 |
| `Meziantou.Analyzer` | 2.0.x | MA-prefix rules for `ConfigureAwait(false)` enforcement (MA0040, MA0004), `CultureInfo` discipline (MA0011), and `Task` patterns (MA0042). Specifically MA0040 / MA0004 carry the BOOTSTRAP §3 `ConfigureAwait(false)` rule into the analyzer. Over `IDisposableAnalyzers`: Meziantou subsumes the dispose-on-return rules and adds more. | BOOTSTRAP §3 |

Drop nothing yet. Skip `SonarAnalyzer.CSharp` (broad, slow, overlaps
Roslynator), `IDisposableAnalyzers` (subsumed), and
`Microsoft.VisualStudio.Threading.Analyzers` (geared to VSIX/RPC
threading, not WebSocket SDKs).

### ASP.NET Core (Phase 5 middleware)

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| ASP.NET Core minimal API + WebSocket middleware (`Microsoft.AspNetCore.App` shared framework) | 9.0.x | Over `Microsoft.Owin` / classic `System.Web`: those don't run ASP.NET Core and are explicitly rejected in Phase 5 of BOOTSTRAP. Configure shape: `IOptions<ArcpOptions>`-based registration (`AddArcp(o => ...)` + `MapArcp("/arcp")`), `app.UseWebSockets()` plus a `MapGet` that accepts on `HttpContext.WebSockets.IsWebSocketRequest`. | BOOTSTRAP Phase 5 |

### CLI

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| `System.CommandLine` | 2.0.0-beta4.22272.1 → **refresh to current beta** (2.0.0-beta5.25xxx) or wait for 2.0 GA | Over `CommandLineParser 2.x` / `Cocona 2.x`: `System.CommandLine` is Microsoft's recommended path and the 2.0 GA wave landed in 2025 (`beta5` shipping with usable shape parity). The pinned `beta4.22272.1` is from 2022 — stale. Pin to the latest published `beta5` for v1.1; plan the 2.0 GA bump when announced. | audit §2 — pre-release pin |

### Build / CPM

| Pick | Version | Why over alternative | Citation |
| --- | --- | --- | --- |
| SDK-style `.csproj` + `Directory.Packages.props` (CPM, transitive pinning on) | n/a | Over `packages.config` / `paket`: CPM lives in the SDK, `CentralPackageTransitivePinningEnabled=true` is already set so every transitive is fixed at the repo root, eliminating dependency drift across the 17 projects. Over `paket`: CPM is native; `paket` is a parallel toolchain the team would have to learn. | `Directory.Packages.props` lines 4–5 |

## Pending refresh — packages already in `Directory.Packages.props`

| Package                                       | From                 | To (target)                       | Reason                                                                                  |
| --------------------------------------------- | -------------------- | --------------------------------- | --------------------------------------------------------------------------------------- |
| `Microsoft.Data.Sqlite`                       | 9.0.0                | 9.0.x latest                      | Hold on the 9.0 line until the library actually multi-targets `net10.0`.                |
| `Microsoft.Extensions.Logging.Abstractions`   | 9.0.0                | 9.0.x latest                      | Same.                                                                                   |
| `Microsoft.Extensions.Logging.Console`        | 9.0.0                | 9.0.x latest (test only)          | Same.                                                                                   |
| `Microsoft.Extensions.TimeProvider.Testing`   | 9.0.0                | 9.0.x latest (test only)          | Same; needed for §9.5 watchdog tests (audit §4 row §9.5).                               |
| `Microsoft.IdentityModel.JsonWebTokens`       | 8.2.0                | 8.x latest (8.6 line)             | Refresh; resume-token path (audit §1 row §6.3) may want newer JWE/JWS bug fixes.        |
| `Ulid`                                        | 1.3.4                | 1.3.4 (current)                   | Already current; no move.                                                               |
| `JsonSchema.Net`                              | 7.2.3                | 7.x latest (tests only)           | Conformance-fixture use only.                                                           |
| `System.CommandLine`                          | 2.0.0-beta4.22272.1  | latest 2.0.0-beta5 (2025 wave)    | Critical refresh: existing pin is 2022-vintage.                                         |
| `xunit`                                       | 2.9.2                | 2.9.3                             | Patch refresh; stay on v2 for v1.1.                                                     |
| `xunit.runner.visualstudio`                   | 2.8.2                | 2.8.3                             | Patch refresh.                                                                          |
| `FluentAssertions`                            | 6.12.2               | **hold 6.12.2, pin `[6.12.2,7.0.0)`** | License-driven: 8.0 went commercial; evaluate `Shouldly` for v1.2.                |
| `Microsoft.NET.Test.Sdk`                      | 17.11.1              | 17.12.x                           | Refresh.                                                                                |
| `coverlet.collector`                          | 6.0.2                | 6.0.2 (current)                   | No move.                                                                                |
| `StyleCop.Analyzers`                          | 1.2.0-beta.556       | hold beta; plan stable migration  | Beta is the only published 1.2; track repo for stable.                                  |
| `Microsoft.SourceLink.GitHub`                 | 8.0.0                | 8.0.0 (current)                   | No move.                                                                                |

New `Directory.Packages.props` entries needed for v1.1 (not adds in
this Phase — flagged here for Phase 4):

- `Roslynator.Analyzers` — `PrivateAssets="all"`, `4.13.x`.
- `Meziantou.Analyzer` — `PrivateAssets="all"`, `2.0.x`.
- `Verify.Xunit` — test only, `26.x`.
- `BenchmarkDotNet` — test only, `0.14.x`.
- `FsCheck.Xunit` — test only, `3.x`.
- `dotnet-stryker` / `dotnet-reportgenerator-globaltool` — global
  tools, declared in `dotnet-tools.json`, not `Directory.Packages.props`.
- `OpenTelemetry.Api` — **only** in `Arcp.Otel` (Phase 5), `1.9.x`.
- `Microsoft.AspNetCore.App` framework reference — implicit in
  `Arcp.AspNetCore` (Phase 5) via `Microsoft.NET.Sdk.Web`.

## Footprint

- Direct library `PackageReference` count for `Arcp.Core` after the
  v1.1 re-key: **2** (`Microsoft.Extensions.Logging.Abstractions`,
  `Ulid`). Everything else (JSON, WebSocket client, HTTP,
  `ActivitySource`, channels, async streams, `Guid.CreateVersion7`)
  is BCL. Resume-token signing keeps
  `Microsoft.IdentityModel.JsonWebTokens` in whichever project owns
  auth — that may be `Arcp.Runtime`, not `Arcp.Core`, depending on
  Phase 4's split.
- Test-project additions: `Verify.Xunit`, `BenchmarkDotNet`,
  `FsCheck.Xunit` — three new pins, all `PrivateAssets="all"` /
  test-only.
- Adapter projects: `Arcp.AspNetCore` adds the
  `Microsoft.AspNetCore.App` framework reference (no NuGet pin);
  `Arcp.Otel` adds `OpenTelemetry.Api` only (no exporter pins).
- Transitive risk: pinned via
  `CentralPackageTransitivePinningEnabled=true`. The single visible
  long pole is the `Microsoft.IdentityModel.*` graph
  (`Microsoft.IdentityModel.JsonWebTokens 8.2.0` pulls
  `Microsoft.IdentityModel.Tokens`, `.Logging`) — keep on the 8.x
  line.
- AOT blockers: none in the picks above when source-gen JSON is on.
  The two surfaces to watch are (1) the
  `welcome.capabilities.agents` rich-union converter (custom
  `JsonConverter`s are AOT-safe if they avoid reflection — written
  by hand they are) and (2) `Microsoft.Data.Sqlite` if/when it lands
  in the event-log store path: it has native dependencies but is
  documented AOT-compatible on .NET 8+. Confirm during Phase 4.
- License watch: `FluentAssertions 8.x` (commercial — pinned out
  above). No other license changes flagged in the picks above.
