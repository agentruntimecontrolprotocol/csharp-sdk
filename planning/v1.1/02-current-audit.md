# 02 — Current-SDK Audit

## TL;DR

The C# SDK does not currently implement ARCP v1.0. It implements an
**earlier internal draft** (`RFC-0001-v2.md`) with a different message
taxonomy (e.g. `session.open`/`session.accepted`/`session.challenge`,
`tool.invoke`, `workflow.start`, `subscribe`/`subscribe.event`,
`stream.open`, `lease.granted`/`lease.revoked`, `checkpoint.create`)
and a 21-member error enum. v1.1 cannot be layered on this directly —
the first PR-sized milestone is **conformance to v1.0 wire format and
taxonomy**; v1.1 features sit cleanly on that base. Treat this as a
re-keying, not a redesign: ULIDs, transports, store, auth, JSON
plumbing are reusable.

Citations below: `src/ARCP/...` paths plus spec §.

## 1. v1.0 conformance vs. this SDK

The TS reference (`../typescript-sdk/CONFORMANCE.md`) tags every v1.0
requirement as Implemented and cites a `packages/...` path. The C#
SDK's own `CONFORMANCE.md` is a 5-line stub. Cross-referencing the
spec § list against `src/ARCP/`:

| Spec §        | Requirement                                                                                    | C# status                                                                                                              |
| ------------- | ---------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| §4.1          | WebSocket text frames, `/arcp` path                                                            | Transport class present (`Transport/WebSocketTransport.cs`); path is deployer-bound, no path negotiation               |
| §4.2          | stdio newline-delimited JSON                                                                   | `Transport/StdioTransport.cs` present                                                                                  |
| §5.1          | `arcp` = `"1"`                                                                                 | `Envelope.Arcp` field exists; runtime accepts the value, but the wire schema cites `RFC-0001-v2 §6.1.1`, not §5.1      |
| §5.1          | `id`, `type`, `session_id`, `trace_id?`, `job_id?`, `event_seq` on event envelopes             | `Envelope.cs` has all (plus 11 extra fields like `Source`, `Target`, `StreamId`, `SubscriptionId`, `Priority`)         |
| §5.1          | unknown top-level fields MUST be ignored                                                       | `EnvelopeJsonConverter` is custom — confirm via test that unknown extras round-trip and don't fail                     |
| §6.1          | Bearer token in `session.hello.payload.auth.token`                                             | Token plumbed via `Auth/BearerAuth.cs`, but the message is `session.open` and the field is `Auth`/`AuthCredential`     |
| §6.2          | `session.hello` ↔ `session.welcome` exchange                                                   | **Missing**: types are named `session.open`, `session.challenge`, `session.accepted`, `session.unauthenticated`        |
| §6.2          | `welcome.payload.capabilities.encodings` / `agents`                                            | **Missing**: `Capabilities` is a boolean grid (`Streaming`, `DurableJobs`, `Checkpoints`, ...) — not the spec shape    |
| §6.3          | Resume token (≥128 bits), rotated every welcome                                                | `Auth/JwtAuth.cs` exists but no resume-token mint/rotate path is in `SessionState`                                     |
| §6.7          | `session.bye { reason }` close                                                                 | Type is `session.close`; payload close                                                                                 |
| §7.1          | `job.submit { agent, input, lease_request?, idempotency_key?, max_runtime_sec? }`              | **Missing**: there is no `job.submit` message type registered                                                          |
| §7.1          | `job.accepted { job_id, lease, accepted_at, ... }`                                             | Type `job.accepted` is registered (`Messages/Execution/ExecutionMessages.cs`) but payload shape differs                |
| §7.2          | Logical idempotency via `payload.idempotency_key`                                              | Envelope has top-level `IdempotencyKey` instead — non-conformant; spec puts it under payload                           |
| §7.3          | Job states `pending`/`running`/`success`/`error`/`cancelled`/`timed_out`                       | No enum found that names these states; `JobManager.cs` is the place to confirm                                         |
| §7.4          | `job.cancel { reason }` → `job.error{ final_status: "cancelled" }` within 30s grace            | Cancellation is a `cancel` envelope type (`Messages/Control/ControlMessages.cs`); grace deadline not in PLAN.md        |
| §8.1          | Single `job.event` envelope with `payload.kind`/`payload.ts`/`payload.body`                    | **Missing**: each event kind is its own envelope type (`tool.invoke`, `tool.result`, `metric`, `log`, `trace.span`)    |
| §8.2          | Reserved kinds `log`, `thought`, `tool_call`, `tool_result`, `status`, `metric`, `artifact_ref`, `delegate` | Partial — `log`, `metric` exist as envelope types; the others are absent or shaped differently                  |
| §8.3          | `event_seq` SESSION-scoped, monotonic, gap-free across reconnects                              | `Store/EventLog.cs` exists; per-session counter is in `Runtime/SessionState.cs` — confirm during re-keying             |
| §9.1          | Lease immutable, granted at submit                                                             | `Runtime/LeaseManager.cs` exists with `lease.granted`/`lease.refresh`/`lease.extended`/`lease.revoked` — non-conformant (v1.0 has no `lease.*` envelopes; lease is a field on `job.accepted`) |
| §9.2          | Reserved namespaces `fs.read`/`fs.write`/`net.fetch`/`tool.call`/`agent.delegate`/`cost.budget` | Not enumerated as constants anywhere I can locate                                                                      |
| §9.4          | Lease subsetting for delegation                                                                | `Runtime/LeaseManager.cs` has subsetting logic — re-key, do not rebuild                                                |
| §10           | `delegate` as event kind on parent's `job.event` stream                                        | **Missing**: `agent.delegate`/`agent.handoff` are top-level envelope types instead                                     |
| §11           | `trace_id` 32-hex; OTel span attrs `arcp.session_id`, `arcp.job_id`, `arcp.agent`              | `Trace/Tracing.cs` exists — confirm attribute names match spec                                                         |
| §12           | 12 canonical error codes (see Phase 1 §12 table)                                               | `Errors/ErrorCode.cs` has 21 members; only `PERMISSION_DENIED`, `HEARTBEAT_LOST`, `LEASE_EXPIRED`, `UNAUTHENTICATED`, `CANCELLED`, `INTERNAL` overlap |
| §13           | Examples (TS ships 23)                                                                         | `samples/` has 14 (names match v1.1 features but predate spec alignment)                                               |

**Net:** The substrate (transports, IDs, JSON converter, event log,
auth plumbing, lease subsetting, capability negotiator skeleton) is
keep-and-reshape work. The wire vocabulary (every `MessageType`
subclass and every entry in `MessageTypeRegistry.CoreCatalog()`),
the `ErrorCode` enum, and the `Capabilities` record are
**delete-and-rewrite** to land v1.0.

## 2. Solution layout

`ARCP.sln` (17 projects, all `net10.0`):

| Project                             | Kind   | TFM       | Refs                                                                              |
| ----------------------------------- | ------ | --------- | --------------------------------------------------------------------------------- |
| `src/ARCP`                          | lib    | `net10.0` | `Ulid 1.3.4`, `Microsoft.Extensions.Logging.Abstractions 9.0.0`, `Microsoft.IdentityModel.JsonWebTokens 8.2.0`, `Microsoft.Data.Sqlite 9.0.0`, `JsonSchema.Net 7.2.3` |
| `src/ARCP.Cli`                      | exe    | `net10.0` | `System.CommandLine 2.0.0-beta4` (pre-release; replace) + ARCP                    |
| `tests/ARCP.UnitTests`              | xUnit  | `net10.0` | `xunit 2.9.2`, `FluentAssertions 6.12.2`, `coverlet.collector 6.0.2`              |
| `tests/ARCP.IntegrationTests`       | xUnit  | `net10.0` | same                                                                              |
| `samples/{14 projects}`             | exe    | `net10.0` | ARCP                                                                              |

Pinned SDK: `10.0.203` (`global.json`, `rollForward: latestFeature`,
`allowPrerelease: false`). Bootstrap says "net9.0 with net10.0 once
LTS lands" — repo is ahead, on net10.0 only. **Decision needed**
(Phase 3/4): drop to multi-TFM (`net9.0;net10.0`) for library
projects so consumers on .NET 9 LTS aren't excluded; keep `net10.0`
on tests/samples. Not free — `Guid.CreateVersion7()` (§5 wire `id`)
is `net9.0+` so this is safe, but every newer BCL pivot needs the
same check.

CPM is on (`Directory.Packages.props`, `ManagePackageVersionsCentrally`
= `true`). Versions are 9.0.x where 10.0.x exists for some MS
packages; refresh as a step in Phase 3.

## 3. Nullable / warnings / analyzers

`Directory.Build.props`:

- `Nullable = enable`
- `TreatWarningsAsErrors = true`
- `EnforceCodeStyleInBuild = true`
- `AnalysisLevel = latest-recommended`, `AnalysisMode = All`
- `CodeAnalysisTreatWarningsAsErrors = true`
- `GenerateDocumentationFile = true`
- `NoWarn = $(NoWarn);CA1014` (suppresses `CLSCompliantAttribute` —
  fine for an internal SDK)

Analyzer stack today is `StyleCop.Analyzers 1.2.0-beta.556` only.
Phase 3 must call: keep StyleCop, add `Microsoft.CodeAnalysis.NetAnalyzers`
(built-in via `AnalysisMode`), and decide on Roslynator vs.
Meziantou for the third slot. `BinaryFormatter` is not referenced;
no reflection-based DI is in the library (good — Phase 4 hard rule
stands without effort).

## 4. v1.1 gap matrix

`{missing/partial/present}` against the v1.1 deltas from Phase 1.
"Risk H/M/L" reflects work, not protocol risk.

| §        | v1.1 feature                                       | C# status   | Target namespace                                                                                                                       | Risk | C#-specific note                                                                                                                                                                                                                  |
| -------- | -------------------------------------------------- | ----------- | -------------------------------------------------------------------------------------------------------------------------------------- | ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| §6.2     | Feature-flag negotiation                           | partial     | `Arcp.Core.FeatureSet`, `Messages/Session/Capabilities.Features`                                                                       | M    | Existing `CapabilityNegotiator` ANDs typed booleans (`Streaming`, `DurableJobs`, ...). v1.1 wants `intersect(string[], string[])`. Different shape — replace, do not extend.                                                       |
| §6.2     | Rich agent inventory `{name, versions[], default?}` | missing     | new `AgentInventoryEntry` `record`; `JsonConverter` to accept either `string[]` or `object[]`                                          | M    | `JsonPolymorphic` does not help here because the discriminator is not on the object; needs a custom `Read`/`Write` that probes `JsonElement.ValueKind`.                                                                            |
| §6.4     | Heartbeat (`session.ping`/`session.pong`)          | partial     | `Messages/Session/SessionPing.cs`, `SessionPong.cs`; runtime timer in `Runtime/SessionState.cs`                                        | M    | `Messages/Control/ControlMessages.cs` already has `Ping`/`Pong` envelope types — different wire types (`"ping"` vs `"session.ping"`). Add the spec types; mark the v0 ones obsolete in the re-keying.                              |
| §6.5     | `session.ack` window flow control                  | partial     | new `SessionAck` record; consumer in `Runtime/SessionState.cs`; `Store/EventLog.cs` trim                                               | M    | `Channel<Envelope>` bounded with `BoundedChannelFullMode.Wait` gives natural backpressure on the outbound side; lag detection still needs explicit lastAck vs. seq.                                                                |
| §6.6     | `session.list_jobs`/`session.jobs`                 | missing     | new `SessionListJobs`, `SessionJobs`; `Runtime/JobManager.ListAsync(IJobAuthorizationPolicy)`                                          | L    | Cursor encoding: opaque base64 of `(createdAt, jobId)` — pure C#, no NuGet.                                                                                                                                                       |
| §7.5     | Agent versioning (`name@version`)                  | missing     | new `AgentRef readonly record struct` in `Arcp.Core`; `IAgentRegistry`                                                                 | L    | Use `record struct` to avoid alloc; `TryParse` mirrors `IParsable<AgentRef>`.                                                                                                                                                     |
| §7.6     | `job.subscribe`/`job.subscribed`/`job.unsubscribe` | partial     | new `Messages/Execution/JobSubscribe.cs`; subscriber fan-out in `Runtime/SubscriptionManager.cs`                                       | M    | `SubscriptionManager.cs` exists but is the v0 `subscribe`/`unsubscribe` envelope path (a different feature). Re-purpose for the v1.1 spec or rename to `JobSubscriptionFanout` and delete v0.                                      |
| §8.2.1   | `progress` event kind                              | missing     | new `ProgressBody` record; reserved-kind list update                                                                                   | L    | Validate `current ≥ 0` and `current ≤ total ?? long.MaxValue` in primary constructor.                                                                                                                                             |
| §8.4     | `result_chunk` event + chunked `job.result`        | missing     | new `ResultChunkBody`; `JobHandle.Chunks(CT)` → `IAsyncEnumerable<ResultChunk>`                                                        | H    | Two pitfalls: (1) `JsonElement` carrying base64 vs. utf8 chunks needs an explicit `byte[]` vs. `string` discriminator — model as `record ResultChunkData(string? Utf8, byte[]? Base64)` with one non-null; (2) `await foreach` must surface back-pressure via the bounded channel from §6.5. |
| §9.5     | `lease_constraints.expires_at`                     | missing     | new `LeaseConstraints` record on `JobSubmit`/`JobAccepted`; `Runtime/LeaseManager.Authorize` deadline                                  | H    | Watchdog: `PeriodicTimer` rooted on `Job.CancellationTokenSource`; `TimeProvider` injected (not `DateTime.UtcNow`) so tests can advance the clock — `Microsoft.Extensions.TimeProvider.Testing` is already on the test path.       |
| §9.6     | `cost.budget` capability + counters                | missing     | `Runtime/LeaseManager`; new `BudgetAmount`, `BudgetLedger`                                                                             | H    | Use `decimal` not `double` for money; `Dictionary<string,decimal>` keyed by currency; `metric` interceptor decrements in `JobManager`. Negative-`value` rejection is an early return, not an exception, per spec.                  |
| §12      | `AGENT_VERSION_NOT_AVAILABLE`                      | missing     | extend `Errors/ErrorCode.cs`, new exception subclass                                                                                   | L    | Trivial after taxonomy re-key.                                                                                                                                                                                                    |
| §12      | `LEASE_EXPIRED`                                    | present (name) | `Errors/ErrorCode.cs:88`                                                                                                            | L    | Already in enum; semantics need attaching to §9.5 watchdog.                                                                                                                                                                       |
| §12      | `BUDGET_EXHAUSTED`                                 | missing     | extend `Errors/ErrorCode.cs`, new exception subclass                                                                                   | L    | Pair with `tool_result` surface, not `job.error`, by default.                                                                                                                                                                     |
| §11      | OTel attrs `arcp.lease.expires_at`, `arcp.budget.remaining` | missing     | `Trace/Tracing.cs`; new `Arcp.Otel` adapter project (Phase 5)                                                                  | L    | `ActivitySource.StartActivity` + `Activity.SetTag` — `System.Diagnostics.DiagnosticSource` only, no OTel SDK reference in the library.                                                                                            |

**H-risk items** name a concrete C# pitfall (above). The single non-feature
H-risk is the wire vocabulary re-keying itself — see §1 of this audit.
That work is `MessageTypeRegistry.CoreCatalog()` rewrite plus deletion
of `Messages/Streaming`, `Messages/Subscriptions`, `Messages/Permissions`
(lease envelopes), `Messages/Control` (most of it), and re-shaping
`Messages/Execution`. Net: ~40 message types out, ~25 in.

## 5. Substrate to keep (with light reshape)

| Path                                                    | Reuse-as                                                                              |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `Ids/*` (ULID + UUIDv7 wrappers)                        | unchanged — `Guid.CreateVersion7()` is BCL; `Ulid` is already pinned                  |
| `Envelope/EnvelopeJsonConverter.cs`                     | keep approach (custom converter, polymorphic dispatch via `MessageTypeRegistry`)      |
| `Envelope/Envelope.cs`                                  | trim — `Source`, `Target`, `StreamId`, `SubscriptionId`, `Priority`, `CausationId` are not v1.0/v1.1 wire fields; move out                                |
| `Store/EventLog.cs`                                     | underpins resume + `session.ack` trim                                                 |
| `Transport/{Memory,WebSocket,Stdio}Transport.cs`        | spec-side §4 transports; `MemoryTransport` is the test transport (matches TS)         |
| `Runtime/LeaseManager.cs`                               | subsetting logic re-keyed to spec namespaces                                          |
| `Runtime/CapabilityNegotiator.cs`                       | replace its boolean intersection with `string[]` intersection                          |
| `Auth/BearerAuth.cs`                                    | keep; v1.0 §6.1 bearer-only                                                           |
| `Trace/Tracing.cs`                                      | rebase attribute names to spec                                                        |

## 6. Substrate to delete

| Path                                                                                       | Reason                                                                |
| ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------- |
| `Messages/Streaming/*` (`stream.open`, `stream.chunk`, `stream.close`, `stream.error`)     | v1.1 explicitly defers "Streaming-token surface for LLM outputs" (§Changes-from-v1.0). Result chunking is its own event kind on `job.event` (§8.4) |
| `Messages/Subscriptions/*` (`subscribe`, `subscribe.accepted`, `subscribe.event`, ...)     | v1.1 subscription is `job.subscribe`/`job.subscribed`/`job.unsubscribe` (§7.6), different shape — replace |
| `Messages/Permissions/*` lease envelopes (`lease.granted`/`lease.refresh`/`lease.extended`/`lease.revoked`) | v1.0/v1.1 carry lease as a field on `job.accepted`; there is no lease envelope |
| `Messages/Control/Backpressure` envelope                                                   | v1.1 expresses back-pressure as a `status { phase: "back_pressure" }` event (§13.2) |
| `Messages/Control/CheckpointCreate`/`CheckpointRestore`                                    | Not in v1.0 or v1.1; checkpoints are not in the spec                  |
| `Messages/Execution/{WorkflowStart, WorkflowComplete, AgentHandoff, JobSchedule, JobProgress, JobHeartbeat, JobCheckpoint, JobStarted, JobFailed, JobCompleted, JobCancelled}` | None of these exist in v1.0/v1.1. The spec has three terminal/event envelopes: `job.accepted`, `job.event`, `job.result`/`job.error`. |
| `Envelope/Priority.cs`                                                                     | No `priority` field in v1.0/v1.1 envelope (§5.1)                       |
| Top-level envelope fields: `Source`, `Target`, `IdempotencyKey`, `Priority`, `CausationId`, `StreamId`, `SubscriptionId` | None are in spec §5.1. `idempotency_key` lives under `job.submit.payload`. |
| `samples/Handoff`, `samples/MCP`, `samples/HumanInput`, `samples/PermissionChallenge`      | Not v1.0 or v1.1 features. `samples/MCP` belongs in a separate adapter (out of scope) |

This is large but mechanical. Phase 10 sequences it.

## 7. Test footprint today

`tests/ARCP.UnitTests` has five spec areas (Envelope, Errors,
Extensions, Ids, Store). `tests/ARCP.IntegrationTests` has five
files plus a `Phase1Placeholder.cs`. None of these test the v1.0
wire shapes (because the SDK does not produce them). After the
re-keying, every test in `EnvelopeSpec` and `IntegrationTests` is
either reusable (Ids, Store/EventLog) or rewritten (Envelope —
unknown-field passthrough, monotonic seq, polymorphic dispatch).

Coverage tooling: `coverlet.collector 6.0.2` is in the test refs; no
`coverlet.runsettings` is present. Phase 7 establishes one.

## 8. Anti-slop self-check

This audit names a concrete C# thing in every paragraph that takes a
position: `JsonElement.ValueKind` probing for the agent-inventory
union, `decimal` vs. `double` for money, `record struct AgentRef`,
`PeriodicTimer` for heartbeats, `Channel<T>` bounded for §6.5,
`TimeProvider` injection for §9.5, `Microsoft.Extensions.TimeProvider.Testing`,
`Guid.CreateVersion7()` for §5 wire `id`. Risks are not generic.

The single biggest open question: **does the team accept the cost of
re-keying the v0 SDK to v1.0 ARCP before v1.1?** If not, the only
honest alternative is to fork the SDK against the spec and abandon
the v0 surface — which is what TS effectively did. Phase 10 names
this as the lead milestone.
