# 10 — Synthesis

Reading order: this one stands alone, but every claim points back at a
phase doc. Read [`01-spec-delta.md`](./01-spec-delta.md) and
[`02-current-audit.md`](./02-current-audit.md) if you only have time
for two.

## 1. Executive summary

ARCP v1.1 (`../spec/docs/draft-arcp-02.1.md`) is a backward-compatible
additive revision of v1.0; the envelope `arcp` field stays `"1"`, and
every new feature is gated by a `session.hello`/`session.welcome`
feature-flag handshake (§6.2). For most SDKs the v1.1 migration is
genuinely additive.

For this SDK it is not. The current C# tree under `src/ARCP/` is
**not on the ARCP v1.0 wire** — it implements an earlier internal
draft (`RFC-0001-v2`) with different message names (`session.open`,
`session.accepted`, `tool.invoke`, `workflow.start`, `subscribe`,
`stream.open`, `lease.granted`/`lease.revoked`, `checkpoint.create`),
a different `Capabilities` shape (typed boolean grid instead of
`{ encodings, features, agents }`), and a 21-member `ErrorCode` enum
instead of the spec's 12. See [`02-current-audit.md`](./02-current-audit.md) §1.

The migration is therefore **two work streams in one milestone
sequence**:

1. **Re-key the wire to v1.0.** Mechanical and large
   (~40 message types out, ~25 in, plus the `Capabilities` replacement
   and the `ErrorCode` collapse). The substrate is reusable as-is:
   IDs, `EnvelopeJsonConverter` approach, transports, `EventLog`,
   `LeaseManager` subsetting, `BearerAuth`, `Trace/Tracing` plumbing
   (audit §5 keep-list).
2. **Layer v1.1 additions.** Nine features, one PR each, each
   guarded by its negotiated feature flag (Phase 1 §1–§13).

Phase 10 of this plan exists to put those two streams onto a single
ordered PR list and resolve the contradictions that fell out across
Phases 3–9.

## 2. Contradictions reconciled

Eight cross-phase decisions to lock in:

| #  | Question                                              | Resolution                                                                                                                                                                                                                                                                | Source          |
| -- | ----------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------- |
| 1  | Target frameworks for the libraries                   | `net9.0;net10.0` for `Arcp.Core`/`Arcp.Client`/`Arcp.Runtime`/`Arcp.AspNetCore`/`Arcp.Otel`/`Arcp`; `net10.0` only for tests, samples, CLI. `Guid.CreateVersion7()` is .NET 9+, which is on the wire (§5.1).                                                                | 03 §intro, 04 §2  |
| 2  | `global.json` pin                                     | Move off `10.0.203` to the GA SDK once .NET 10 GA's (Nov 2025; the user is operating in 2026-05). Phase 3 calls the pin "preview"; it isn't anymore, but the bump goes with M1.                                                                                            | 03 §intro         |
| 3  | "18 vs 23" examples count                             | BOOTSTRAP's "18" meant `9 v1.0 + 9 v1.1 = 18 feature examples`. The 4 TS host-integration examples collapse to **one** C# `samples/AspNetCore/` because there is no Express/Fastify/Hono/Bun split on .NET. Net C# sample count: **21**.                                  | 06 §0             |
| 4  | gRPC pass-through adapter                             | **Dropped.** HTTP/2 framing does not match §4.1 WebSocket text frames; no TS analogue earns its keep. `Bun.serve` parity exists in TS because Bun's WS stack ≠ Node's; no .NET split exists.                                                                              | 05 §1             |
| 5  | `Arcp.Hosting` generic-host bootstrapper              | **Keep** as a small (~30 LOC) adapter. Without it the only on-ramp is `WebApplication`, which is wrong for Windows Service / console workers.                                                                                                                              | 05 §1             |
| 6  | `FluentAssertions` v8 license                         | **Pin ≤7.x.** Phase 3 floats `Shouldly` as a swap; Phase 7 defers. Locking in: stay on `FluentAssertions 7.x` for M1; revisit only if the v7 line is abandoned. Swap to `Shouldly` is a follow-up, not a blocker.                                                          | 03 §testing, 07 §1 |
| 7  | `[JsonPolymorphic]` vs custom converter                | **Both.** `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]` handles per-message dispatch and pairs with source-gen. The custom `EnvelopeJsonConverter` survives **only** for the `welcome.capabilities.agents` rich-union (`string[]` ∨ object-array) where the discriminator is shape, not key — `JsonElement.ValueKind` probe. | 03 §json, 04 §3   |
| 8  | Migration from `src/ARCP/` to `src/Arcp.Core/` et al. | **Hard cut**, not a type-forward shim. A shim freezes the v0 vocabulary into the package surface and guarantees its accidental reuse. The rename is in the same PR that lands the v1.0 wire.                                                                              | 04 §1             |

## 3. Ordered milestones

Each milestone is one (or, where called out, a small chain of) PR-sized
changes. Versioning rule from Phase 8: M1 ships as `0.2.0`; v1.1
features ship as `0.3.0`…`0.11.0` (one per feature); the umbrella
hits `1.0.0` when ARCP v1.1 GAs and all 9 features are negotiated.

### M0 — Planning lockdown (this PR)

- Files: the 10 docs under `planning/v1.1/`.
- Reviewers ratify §1–§8 and Phase 4's project split.
- Output: green-light on the project rename and the wire re-key.

### M1 — v1.0 wire re-key (single PR, large)

- Spec: §4, §5, §6.1, §6.2 (v1.0 portion), §6.3, §6.7, §7.1–§7.4, §8.1–§8.3, §9.1–§9.4, §10, §11, §12 (12-code v1.0 set).
- Files:
  - Rename `src/ARCP/` → `src/Arcp.Core/` (Pascal case; matches TS).
  - Delete `Messages/Streaming/*`, `Messages/Subscriptions/*`,
    `Messages/Permissions/{LeaseGranted,LeaseRefresh,LeaseExtended,LeaseRevoked}.cs`,
    `Messages/Control/{Backpressure,CheckpointCreate,CheckpointRestore}.cs`,
    `Messages/Execution/{WorkflowStart,WorkflowComplete,AgentHandoff,JobSchedule,JobProgress,JobHeartbeat,JobCheckpoint,JobStarted,JobFailed,JobCompleted,JobCancelled}.cs`,
    `Envelope/Priority.cs`.
  - Trim `Envelope/Envelope.cs` of non-spec fields (`Source`, `Target`, `IdempotencyKey`, `Priority`, `CausationId`, `StreamId`, `SubscriptionId`).
  - Replace `Messages/Session/{SessionOpen,SessionAccepted,...}` with
    `SessionHello`, `SessionWelcome`, `SessionBye`.
  - Replace `Messages/Execution/*` with `JobSubmit`, `JobAccepted`,
    `JobEvent` (kind-based body), `JobResult`, `JobError`, `JobCancel`.
  - Replace `Errors/ErrorCode.cs` 21-member enum with the v1.0 12-code
    string-typed taxonomy + matching `ArcpException` subclasses
    (Phase 4 §5). Codes: `PERMISSION_DENIED`, `LEASE_SUBSET_VIOLATION`,
    `JOB_NOT_FOUND`, `DUPLICATE_KEY`, `AGENT_NOT_AVAILABLE`,
    `CANCELLED`, `TIMEOUT`, `RESUME_WINDOW_EXPIRED`, `HEARTBEAT_LOST`,
    `INVALID_REQUEST`, `UNAUTHENTICATED`, `INTERNAL_ERROR`.
  - Replace `Messages/Session/Capabilities.cs` boolean grid with the
    spec shape: `{ encodings: string[], agents: ... }` (no `features`
    yet — that's M3).
  - Rewrite `MessageTypeRegistry.CoreCatalog()` to the v1.0 set.
  - Rewrite `Runtime/CapabilityNegotiator.cs` against the new
    `Capabilities` (no booleans).
  - Move `idempotency_key` from envelope to `JobSubmit.Payload`.
  - Resume token: add the mint + rotate path in `SessionState`
    (audit §1 row §6.3).
  - Update `tests/ARCP.UnitTests/EnvelopeSpec/EnvelopeTests.cs` for
    the unknown-field passthrough invariant; rewrite
    `tests/ARCP.IntegrationTests/*` against v1.0 flows.
  - `samples/`: delete `Handoff`, `MCP`, `HumanInput`,
    `PermissionChallenge`, `LeaseRevocation`, `ReasoningStreams`;
    rename `Heartbeats/` → `Heartbeat/`, `Subscriptions/` → `Subscribe/`
    (contents rewritten in M3 / M6).
- Version: `0.2.0`.

### M2 — Project split (small PR)

- Spec: n/a (structural).
- Files: split the single `Arcp.Core.csproj` into `Arcp.Core`,
  `Arcp.Client`, `Arcp.Runtime`, plus the umbrella `Arcp.csproj`
  (TypeForwardedTo only). Phase 4 §1.
- Move `Client/*` to `Arcp.Client`; move `Runtime/*` to `Arcp.Runtime`.
- Wire `[InternalsVisibleTo]` for tests.
- Version: `0.2.1`.

### M3 — Capability negotiation (§6.2 v1.1 portion)

- Adds `features: string[]` on hello + welcome.
- Adds rich `agents: [{ name, versions[], default? }]` shape via the
  shape-discriminated custom converter (Phase 4 §3, decision #7).
- New `Arcp.Core.FeatureSet` static (intersect + `HasFeature`).
- Sample: `samples/CapabilityNegotiation/` (the rename of the
  current v0 sample).
- Tests: feature-intersection unit + conformance row.
- Version: `0.3.0`.

### M4 — Heartbeats (§6.4)

- Feature flag `heartbeat`. New `SessionPing`/`SessionPong` records.
  `welcome.heartbeat_interval_sec` advertised. `PeriodicTimer` driven
  by `TimeProvider`; ping/pong NOT counted in `event_seq`.
- Sample: `samples/Heartbeat/`.
- Version: `0.4.0`.

### M5 — Ack & back-pressure (§6.5)

- Feature flag `ack`. New `SessionAck`. `Channel<Envelope>` bounded
  with `BoundedChannelFullMode.Wait` on the outbound side. Lag-detect
  threshold → `status { phase: "back_pressure" }` event.
- Sample: `samples/AckBackpressure/`.
- Version: `0.5.0`.

### M6 — Job listing (§6.6)

- Feature flag `list_jobs`. New `SessionListJobs`/`SessionJobs`.
  `IJobAuthorizationPolicy` extension point (defaults to same-principal).
- Cursor: opaque base64 of `(createdAt, jobId)`.
- Sample: `samples/ListJobs/`.
- Version: `0.6.0`.

### M7 — Subscribe (§7.6)

- Feature flag `subscribe`. New `JobSubscribe`/`JobSubscribed`/`JobUnsubscribe`.
- Reuses `IJobAuthorizationPolicy` from M6.
- Fan-out: each replayed event uses the **subscriber's** session-scoped
  `event_seq` (Phase 1 §6).
- Subscribers MUST NOT cancel — guarded in `JobManager.Cancel`.
- Sample: `samples/Subscribe/`.
- Version: `0.7.0`.

### M8 — Agent versioning (§7.5)

- Feature flag `agent_versions`. New `AgentRef readonly record struct`
  (`IParsable<AgentRef>`), `IAgentRegistry` indexed by `(name, version)`.
- New error `AGENT_VERSION_NOT_AVAILABLE`.
- Sample: `samples/AgentVersions/`.
- Version: `0.8.0`.

### M9 — Lease expiration (§9.5)

- Feature flag `lease_expires_at`. New `LeaseConstraints { ExpiresAt }`
  on `JobSubmit`/`JobAccepted`.
- Watchdog: `PeriodicTimer` rooted on the job's `CancellationTokenSource`,
  driven by `TimeProvider` (tests use `FakeTimeProvider`).
- New error `LEASE_EXPIRED` (already in v0 enum — repurpose).
- Sub-lease constraint check added to `LeaseManager.AssertSubset`.
- Sample: `samples/LeaseExpiresAt/`.
- Version: `0.9.0`.

### M10 — Budget capability (§9.6)

- Feature flag `cost.budget`. New `BudgetAmount` parser, `BudgetLedger`
  (`Dictionary<string,decimal>`, currency-keyed).
- `metric` interceptor in `JobManager` decrements counters on
  `name: "cost.*"` events. Negative-value: silent reject.
- Surfaces as `tool_result.error` by default, `job.error` only when fatal.
- Debounced `cost.budget.remaining` metric (5% rule, matches TS).
- New error `BUDGET_EXHAUSTED`.
- Sample: `samples/CostBudget/`.
- Version: `0.10.0`.

### M11 — Progress + Result streaming (§8.2.1, §8.4)

- Single PR — both are event-kind additions on the same `job.event` envelope.
- New `ProgressBody`, `ResultChunkBody`. Reserved-kind list adds two.
- `JobHandle.Chunks(CT)` → `IAsyncEnumerable<ResultChunk>`.
- `JobResult.ResultId`/`ResultSize` nullable additions.
- Server-side invariant: inline result XOR chunked, never both.
- Sample: `samples/ResultChunk/` (chunks + progress in one demo,
  matching TS `result-chunk/`).
- Version: `0.11.0`.

### M12 — `Arcp.AspNetCore` adapter

- `IEndpointRouteBuilder.MapArcp("/arcp", opts => ...)`.
- `app.UseWebSockets()` + endpoint; Host-header / `AllowedHosts`
  DNS-rebind defense; `WebSocketOptions.KeepAliveInterval = Timeout.InfiniteTimeSpan`
  to avoid colliding with our app-level §6.4 heartbeat (Phase 5 §2.1).
- `IOptions<ArcpOptions>` shape.
- `Arcp.Hosting` `IHostedService` extension (decision #5).
- Sample: `samples/AspNetCore/`.
- Version: `0.12.0`.

### M13 — `Arcp.Otel` adapter

- Spec §11 v1.1 attrs (`arcp.lease.expires_at`, `arcp.budget.remaining`).
- W3C traceparent over the envelope `extensions["x-vendor.opentelemetry.tracecontext"]`.
- `ActivitySource` per envelope; attribute names match TS exactly.
- Sample: `samples/Tracing/`.
- Version: `0.13.0`.

### M14 — Conformance test harness

- New `tests/ARCP.Conformance/` (Phase 7 §2): one `[Fact]` or
  `[Theory]` row per spec § requirement. `CONFORMANCE.md` is derived
  from the test attributes (optional, follow-up — not blocking).
- Coverage gate hits 87% lines + branches.
- Version: `0.14.0`.

### M15 — Docs site + diagrams

- `docs/` tree (Phase 8 §1) — 19 markdown files.
- DocFX generated API reference cross-linked.
- `docs/diagrams/` — 6 Graphviz pairs, `make` renders SVG.
- README rewrite (Phase 8 §5).
- Version: `0.15.0`.

### M16 — `1.0.0` GA

- Cuts when ARCP v1.1 GAs upstream.
- CHANGELOG closes the v0→v1.0 re-key paragraph and the per-feature
  list. Conformance harness reports all-Implemented.

## 4. Risks (named, not generic)

| Risk                                                                                           | Why it's concrete                                                                                                                                                                                                | Mitigation                                                                                                                                                                       |
| ---------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ClientWebSocket` does not surface the 101 upgrade `HttpResponseMessage`                       | v1.1 §6.2 capability headers need read access to the upgrade response. Phase 3 §websocket-client documents the `HttpClient.SendAsync` + `WebSocket.CreateFromStream` workaround.                                  | Workaround landed inside `Arcp.Client.Internal.WebSocketUpgrade`. Tested against a loopback Kestrel that echoes the headers. M3.                                                  |
| `JsonUnknownTypeHandling` semantics for §5.1 unknown-fields-ignored                            | The spec MUST is unconditional; `System.Text.Json` defaults to `Skip`, which round-trips silently *and* drops the data. Extension-data `JsonElement` preserves it for re-emit.                                    | Per Phase 4 §3, every record carries `[JsonExtensionData] IDictionary<string, JsonElement>? Extensions { get; init; }` for envelope and payload; covered by an Envelope unit test in M1. |
| Multi-TFM `net9.0;net10.0` cost                                                                 | Conditional `#if NET10_0_OR_GREATER` blocks creep in if features land on net10 first (e.g. a new BCL `WebSocket` overload).                                                                                       | Tolerate `#if` only when a BCL surface differs; never branch on it for spec semantics. Lint via Roslynator/Meziantou (Phase 3 §lint).                                              |
| Stryker.NET runtime budget                                                                     | A naive Stryker run mutates everything and blows the nightly slot. Phase 7 specifies "budgeted" without a number.                                                                                                | Baseline run scopes to `Arcp.Core` only; budget capped at 30 min. Expand to `Arcp.Runtime` once the kill-score stabilises.                                                       |
| `FluentAssertions 7.x` end of life                                                              | The v7 line could be archived. Tests would break on a security CVE patch.                                                                                                                                        | Open question #1 below. Swap to `Shouldly` is mechanical; the impact is on assertion ergonomics, not on coverage or kill-scores.                                                  |
| `samples/` count drift                                                                         | M3–M11 each touch a sample; if the wire shape lands wrong, the sample regresses silently.                                                                                                                        | Each milestone's CI runs every sample's exit-0 check via `dotnet run --launch-profile {server,client}` in a single workflow (Phase 6 §3).                                          |
| `record struct AgentRef` value-equality + `JsonConverter`                                       | `record struct` value-equality on a struct that contains a `string` is reference-equality on the string content — fine. But `IParsable<T>.TryParse` returning an out struct + `System.Text.Json` need a converter that's source-gen friendly. | `JsonConverterFactory` registered for `AgentRef`; source-gen-safe converter generated. Covered by M8 conformance row.                                                              |

## 5. Non-goals (lifted from spec §"Not in v1.1")

- Job pause/unpause.
- Job priority and scheduling hints (current `Envelope/Priority.cs`
  is deleted in M1).
- Federation across runtimes.
- Streaming-token surface for LLM outputs (current
  `Messages/Streaming/*` is deleted in M1; this is **not**
  `result_chunk`).
- HITL surfaces (current `Messages/Human/*` and `samples/HumanInput/`,
  `samples/PermissionChallenge/` deleted in M1 — out of v1.1 scope).
- MCP adapter (`samples/MCP/` deleted in M1 — belongs in a separate
  adapter project, out of scope here).

## 6. Open questions (three, max)

1. **FluentAssertions v8 stance.** Current pin is `6.12.2`. v8 is
   commercial-licensed. Phase 3 + Phase 7 defer. Recommendation: pin
   `7.x` for the v1.0 wire (M1) and re-evaluate at M14 when the
   conformance harness lands. Swap to `Shouldly` if `7.x` is archived
   before M14.

2. **Multi-TFM scope.** Phase 3 + Phase 4 say `net9.0;net10.0` for
   libraries. The current code is `net10.0`-only. Concrete cost: every
   `System.Net.Http` / `System.Text.Json` API used must be verified
   present in 9.0 (`Guid.CreateVersion7()` is; some newer overloads
   aren't). Decision input needed from the .NET-version policy owner;
   if `net9.0` is dropped, M1 trims to `net10.0` only.

3. **`Arcp.Hosting` ship-in-v1 or defer.** Phase 5 keeps it as a
   small adapter (~30 LOC). The question: ship it inside M12 alongside
   `Arcp.AspNetCore`, or defer to a post-1.0 release. Recommendation:
   ship in M12 — without it the only on-ramp is `WebApplication`,
   which excludes Windows Service / worker scenarios that the TS SDK
   doesn't even need to think about (Node has no equivalent split).

## 7. What "done" looks like

When M16 ships:

- `ARCP.sln` has 7 library projects (`Arcp`, `Arcp.Core`, `Arcp.Client`,
  `Arcp.Runtime`, `Arcp.AspNetCore`, `Arcp.Otel`, `Arcp.Hosting`),
  one CLI (`Arcp.Cli`), 5 test projects, 21 sample projects.
- All 9 v1.1 feature flags negotiate, run, and exit-0 in their samples.
- `CONFORMANCE.md` (auto-derivable from `tests/ARCP.Conformance/`)
  reports Implemented for every v1.0 + v1.1 row.
- Coverage ≥ 87% lines AND branches.
- `dotnet add package Arcp` + the 20-line README quickstart compile
  and run end-to-end against a loopback runtime.
- TS feature parity: every TS conformance row has a C# row pointing
  at a file path under `src/Arcp.*/`.

## 8. What this plan deliberately did not do

- Write any `.cs`. Per BOOTSTRAP "Operating rules" — plan, don't build.
- Pick the exact Stryker kill-score baseline (it depends on M1 landing first).
- Re-derive each spec § from scratch — the spec is the spec. Plans
  cite it; they do not paraphrase it.
- Argue about the v0 design. The audit (§6) names what's deleted; the
  reason is "not in the spec," not "bad design."

End of plan. Next action: lock decisions #1–#8 in §2; cut M1 as the
opening PR.
