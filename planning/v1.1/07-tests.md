# 07 — Test Plan

Coverage floor (per `BOOTSTRAP.md` Phase 7): **87% lines AND 87%
branches** measured by `coverlet.collector 6.0.2` + `reportgenerator`,
gated in CI. Mutation testing runs nightly via `Stryker.NET` against
the same source set; the kill score is informational at first, gating
once a baseline lands (see Phase 3).

This plan only specifies what to test and how to organize tests. No
test code is written here. Every layer cites a spec §, a current C#
path under `/Users/nficano/code/arpc/csharp-sdk/`, or a named NuGet.

## 1. Stack

| Concern               | Pick                                                                | Why                                                                                                                       |
| --------------------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Test framework        | `xUnit 2.9.2` (pinned in `Directory.Packages.props`)                | Already pinned; `[Fact]`/`[Theory]` cover the conformance-row pattern below                                               |
| Assertions            | `FluentAssertions 6.12.2` — **pin ≤7.x** or swap to `Shouldly`      | v8 changed license to commercial; defer the final pick to Phase 3                                                         |
| Snapshots             | `Verify.Xunit` (NuGet `Verify.Xunit`)                               | Envelope wire-bytes round-trip; pretty-prints JSON; `received.txt`/`verified.txt` workflow                                |
| Coverage              | `coverlet.collector 6.0.2` + `dotnet-reportgenerator-globaltool`    | `coverlet.collector` already in test refs; reportgenerator produces Cobertura for CI + HTML for humans                    |
| Mutation              | `Stryker.NET` (nightly, budgeted)                                   | Defer baseline kill-score to Phase 3                                                                                      |
| Clock                 | `Microsoft.Extensions.TimeProvider.Testing 9.0.0` (already pinned)  | §6.4 heartbeat, §9.5 lease watchdog, §9.6 budget debounce, §6.5 ack window all depend on `TimeProvider`                   |
| ASP.NET host          | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<TStartup>`) | Loopback Kestrel for `Arcp.AspNetCore` middleware tests                                                                 |
| Property              | `FsCheck.Xunit` — argued in §9 below                                | Decision recorded there                                                                                                   |
| Benchmarks            | `BenchmarkDotNet`                                                   | Informational only, not in the CI required path                                                                           |

**Fakes over `NSubstitute` for state-machine code.** `Runtime/SessionState.cs`,
`Runtime/JobManager.cs`, the §9.5 lease watchdog, and the §6.4
heartbeat timer are FSMs. NSubstitute-style tests assert call
*sequences* — but the spec rules are *invariants* ("a job that has
emitted `job.error` MUST NOT subsequently emit `job.event`"). Asserting
sequence brittle-tests the spec rules: a refactor that preserves the
invariant fails the test. Fakes (a hand-written `FakeTransport`
that captures emitted envelopes in order, plus a `FakeTimeProvider`)
let tests assert on the *trace of envelopes* against the invariant,
not on which methods of which collaborator were called. Reason
recorded per `BOOTSTRAP.md` operating rules. `NSubstitute` is fine
for stateless seams (e.g. `IAgent.RunAsync` returning a canned
`IAsyncEnumerable<JobEventBody>`).

## 2. Test project layout

```
tests/
  ARCP.UnitTests/           (keep — re-keyed to v1.0/v1.1 wire)
  ARCP.IntegrationTests/    (keep — re-keyed; loopback `MemoryTransport`)
  ARCP.Conformance/         (NEW — one test per CONFORMANCE.md row)
  Arcp.AspNetCore.Tests/    (NEW — loopback Kestrel + WebApplicationFactory)
  ARCP.Benchmarks/          (NEW — BenchmarkDotNet, off the CI required path)
```

- **`ARCP.UnitTests`** — kept from Phase-2 audit (5 areas: Envelope,
  Errors, Extensions, Ids, Store). Re-keyed: every test that asserts
  the v0 `session.open`/`tool.invoke`/`stream.open` taxonomy is
  rewritten against v1.0 names.
- **`ARCP.IntegrationTests`** — kept (5 files + the
  `Phase1Placeholder.cs` stub). After the re-keying, the placeholder
  becomes the §6.4 heartbeat smoke test; the other four become the
  v1.0 example flows in §3 below.
- **`ARCP.Conformance`** — NEW. Mirrors the TS pattern: each row of
  `CONFORMANCE.md` becomes one `[Fact]` (or one `[Theory]` row) whose
  display name is the spec § + requirement string. Tests target the
  public surface only (`Arcp.Core`, `Arcp.Client`, `Arcp.Runtime`).
  This lets `CONFORMANCE.md` be **derived from test attributes** by a
  small generator (optional follow-up; the attribute would be
  `[ConformanceFact("§7.1", "job.submit MUST carry agent and input")]`).
- **`Arcp.AspNetCore.Tests`** — NEW. Boots the middleware adapter
  defined in Phase 5 (`Arcp.AspNetCore`) inside
  `WebApplicationFactory<TStartup>`, dials it over a real
  `System.Net.WebSockets.ClientWebSocket`, runs the §13 flows. Catches
  Host-header guard, WS upgrade, `IOptions<ArcpOptions>` shape, and
  the `MapArcp("/arcp")` endpoint wiring.

## 3. Layered plan

Layering goes from cheapest assertions outward; a failure in a lower
layer should make the corresponding higher-layer failure obvious. Run
all five in every PR.

### Layer 1 — Envelope (`tests/ARCP.UnitTests/Envelope/`)

Targets `src/ARCP/Envelope/Envelope.cs` and
`src/ARCP/Envelope/EnvelopeJsonConverter.cs`.

- **JSON round-trip.** Each registered v1.0/v1.1 message type
  (`session.hello`, `session.welcome`, `session.ping`, `session.pong`,
  `session.ack`, `session.list_jobs`, `session.jobs`, `session.bye`,
  `job.submit`, `job.accepted`, `job.event`, `job.result`, `job.error`,
  `job.cancel`, `job.subscribe`, `job.subscribed`, `job.unsubscribe`)
  goes through one `[Theory]` row asserting deserialize ∘ serialize is
  the identity.
- **Unknown-field passthrough (spec §5.1).** "v1.1 implementations
  MUST ignore unknown top-level envelope fields." Test injects a
  bogus `foo: 1` at the envelope level and at the payload level and
  asserts: (a) no throw, (b) the parsed envelope still equals the
  same object without `foo`. This is the single test the audit calls
  out as missing today.
- **Polymorphic dispatch.** `EnvelopeJsonConverter` reads `type`,
  looks up the payload-deserializer in `MessageTypeRegistry`; one
  `[Fact]` per type asserts the converter picks the correct
  `record`. One negative test asserts an unknown `type` does NOT
  throw (forward-compat) and returns an `UnknownEnvelope` shape.
- **Verify snapshots.** Canonical wire bytes for *at least one*
  example of each message kind, stored as
  `tests/ARCP.UnitTests/Envelope/snapshots/<type>.verified.txt`.
  Pretty-printed JSON; hash mode for any base64 payload over 1 KB.

### Layer 2 — Message records (`tests/ARCP.UnitTests/Messages/`)

Per-payload validation. One `[Theory]` per rule, one row per
spec-cited constraint.

- `ProgressBody` (§8.2.1): `current < 0` throws
  `INVALID_REQUEST`; `total < current` throws.
- `LeaseConstraints.ExpiresAt` (§9.5): non-UTC (`+00:00` offset that
  isn't `Z` — `DateTimeOffset.Offset != TimeSpan.Zero` after parse)
  throws; past-relative-to-`TimeProvider` throws.
- `BudgetAmount.Parse` (§9.6): grammar `currency:decimal` accepted;
  negative or non-decimal rejected; uses `decimal`, not `double` (a
  test row with `USD:0.1` + `USD:0.2` asserts the sum is exactly
  `0.3m`, not `0.30000000000000004`).
- `AgentRef.Parse` (§7.5): `name` and `name@version` accepted;
  empty name, empty version, illegal chars per `[a-zA-Z0-9.+_-]+`
  rejected.
- `ResultChunkBody` (§8.4): `encoding ∈ {utf8, base64}`; `chunk_seq`
  ≥ 0; `data` matches `encoding` (utf8 → `string` non-null;
  base64 → `byte[]` non-null; never both).
- `JobResult` (§8.4): cannot co-emit inline `result` and
  `result_chunk` for the same job — enforced by the writer side, not
  the record; tested at Layer 3.
- `Envelope` top-level (§5.1): missing `arcp` rejected; `arcp != "1"`
  rejected; `event_seq` required when `type` starts with `job.`
  (event/result/error) and forbidden otherwise.

### Layer 3 — Session / Job FSM (`tests/ARCP.UnitTests/Runtime/`)

Targets `Runtime/SessionState.cs` and `Runtime/JobManager.cs`. **Assert
invariants, not call sequences.**

- **Job FSM** (§7.3): `pending → running → {success | error |
  cancelled | timed_out}`. One `[Theory]` per illegal transition
  asserts the manager throws or no-ops; one positive `[Fact]` per
  legal terminal asserts the *trace* emitted by a `FakeTransport`
  ends with the correct terminal envelope and no further envelopes
  follow.
- **Session FSM**: `hello → welcome → open → bye | resume`. Tests
  drive the FSM via the public surface (`ArcpClient.ConnectAsync`,
  `Session.SendAsync`, `Session.CloseAsync`) and assert the welcome
  capability `features` intersection matches §6.2.
- **§6.4 invariant**: `session.ping`/`session.pong` are **excluded
  from `event_seq`**. Test runs a job, lets the heartbeat timer fire
  (via `FakeTimeProvider.Advance(TimeSpan.FromSeconds(30))`), asserts
  the next `job.event.event_seq` is contiguous with the last one
  before the ping.
- **§6.5 invariant**: `session.ack` is excluded from `event_seq` and
  `EventLog.TrimBeforeAsync` is advisory — replaying after a resume
  yields all events the resume window covers regardless of acks.
- **§9.5 invariant**: at the `expires_at` instant the watchdog emits
  `job.error { code: "LEASE_EXPIRED" }` and no `job.event` is emitted
  after it. Drive time with `FakeTimeProvider`.
- **§9.6 invariant**: monotone-decreasing budget — applying a metric
  with `value > 0` decrements the counter; `value < 0` is rejected
  silently (no envelope emitted). After the counter crosses zero,
  the next operation in the gated namespace produces
  `BUDGET_EXHAUSTED` as a `tool_result.error` (preferred surface,
  per Phase 1 §10).

### Layer 4 — Integration (`tests/ARCP.IntegrationTests/`)

Two transports: `Transport/MemoryTransport.cs` (in-proc, the TS-parity
test transport) and `Transport/WebSocketTransport.cs` over loopback
Kestrel (lives in `Arcp.AspNetCore.Tests` — see §2). **No mocks.**

The cross-product of {v1.0, v1.1} examples × {MemoryTransport,
WebSocketTransport} produces the matrix. Marked `[Trait("layer","integration")]`.

**v1.0 example flows** — each mirrors §13 of `draft-arcp-02.md`
(v1.0 examples remain illustrative per §13 of v1.1):

| Flow                   | Spec §          | What it asserts                                                                                   |
| ---------------------- | --------------- | ------------------------------------------------------------------------------------------------- |
| `submit-and-stream`    | §7.1 + §8.1     | `job.submit` → `job.accepted` → `N × job.event` → `job.result`, `event_seq` contiguous            |
| `delegate`             | §10             | Parent emits `delegate` event kind; child job has `parent_job_id`; lease subset asserted          |
| `resume`               | §6.3            | Drop the transport mid-stream, reconnect with `resume_token`, all events replayed in order        |
| `idempotent-retry`     | §7.2            | Two `job.submit` envelopes with the same `payload.idempotency_key` resolve to the same `job_id`   |
| `lease-violation`      | §9.2 + §12      | Operation outside `lease_request` produces `PERMISSION_DENIED` as `tool_result.error`             |
| `cancel`               | §7.4            | `job.cancel` → `job.error { final_status: "cancelled" }` within 30s grace via `FakeTimeProvider`  |
| `stdio`                | §4.2            | Same flow over `Transport/StdioTransport.cs`, newline-delimited JSON                              |

**v1.1 example flows** — every §13 sub-section of `draft-arcp-02.1.md`:

| Flow                | Spec §   | What it asserts                                                                                       |
| ------------------- | -------- | ----------------------------------------------------------------------------------------------------- |
| `heartbeat`         | §13.1    | After 2× negotiated interval of silence, the watchdog fires `session.ping`; pong inside window keeps  |
|                     |          | the session open; missed pong closes the transport with `HEARTBEAT_LOST` but the job keeps running    |
| `ack`               | §13.2    | `session.ack { last_processed_seq }` trims the event log; sustained lag emits `status { phase: ...}`  |
| `list_jobs`         | §13.3    | Second session under the same principal lists the first session's running job                        |
| `subscribe`         | §13.3    | Subscribing with `history: true` replays buffered events under the subscriber's session `event_seq`   |
| `agent-versions`    | §13.7    | `code-refactor@1.0.0` resolves; `@3.0.0` produces `AGENT_VERSION_NOT_AVAILABLE`                       |
| `lease-expires-at`  | §13.4    | Watchdog emits `tool_result.error { LEASE_EXPIRED }` then `job.error` at `expires_at` (FakeTimeProvider) |
| `cost-budget`       | §13.5    | Metric-driven decrement; sub-lease child budget cannot exceed parent remaining (§9.4 addendum)        |
| `progress`          | §8.2.1   | `progress` event delivered; protocol ignores its content                                              |
| `result-chunk`      | §13.6    | `IAsyncEnumerable<ResultChunk>` consumed via `await foreach`; reassembled bytes match expected hash;  |
|                     |          | inline `result` and `result_chunk` are mutually exclusive (server-side guard)                         |

These tests **must** run the real transport — they exist to catch the
seams that unit tests cannot: WebSocket close codes, JSON serializer
context registration, ASP.NET routing.

### Layer 5 — Conformance harness (`tests/ARCP.Conformance/`)

One test per row in `CONFORMANCE.md`. Display name is
`{spec_section}: {requirement_string}` so a failing CI report reads
like a spec compliance report. The harness asserts behaviour against
the public surface only; it does not reach into internals. Adding a
new spec requirement is a two-line PR: add the row to
`CONFORMANCE.md`, add the `[ConformanceFact(...)]`. The generator
follow-up (mentioned in §2) is what makes the markdown and the test
file inseparable.

## 4. Cancellation tests

Every public async method that takes a `CancellationToken` gets one
cancellation test:

```text
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromMilliseconds(50));
await Assert.ThrowsAnyAsync<OperationCanceledException>(
    () => sut.MethodAsync(cts.Token));
```

**Trap**: BCL libraries throw either `OperationCanceledException` *or*
`TaskCanceledException` (the latter derives from the former). The
assertion **must** be on the base — `ThrowsAnyAsync<OperationCanceledException>`,
not `ThrowsAsync<TaskCanceledException>`. A test that pins the
derived type will pass on `Task.Delay` and fail on `Channel<T>.Reader.ReadAsync`,
which is a flake we are not adding back.

Surfaces requiring this test (from Phase-4 sketch — file paths once
Phase 4 lands): `ArcpClient.ConnectAsync`, `Session.SendAsync`,
`Session.CloseAsync`, `Job.SubmitAsync`, `JobHandle.Events(CT)`
(stops the `await foreach`), `JobHandle.Chunks(CT)` (§8.4),
`JobHandle.CancelAsync`, `ITransport.SendAsync`, `ITransport.ReceiveAsync`,
`EventLog.AppendAsync`, `EventLog.ReadAsync`, `EventLog.TrimBeforeAsync`.

## 5. Time-dependent tests

`Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` (pinned
`9.0.0` in `Directory.Packages.props`) is injected wherever the
runtime reads "now". Wall-clock waits are forbidden in tests.

Pin to `FakeTimeProvider` (not `Task.Delay`):

- §6.4 heartbeat watchdog — `PeriodicTimer` constructed with
  `TimeProvider`; tests advance via `Advance(TimeSpan.FromSeconds(31))`.
- §6.5 ack window debounce.
- §9.5 lease watchdog — the `expires_at` instant is reachable by
  advancing the fake clock; no flake from CI scheduler jitter.
- §9.6 budget metric debounce (5% rule per Phase 1 §10).

Test harness rule: if a `Task.Delay` shows up in a test, it gets
flagged in review. `Channel<T>` reads or `await foreach` with
`FakeTimeProvider.Advance` are the supported patterns.

## 6. CI matrix

| Axis      | Values                                  | Why                                                                                                                                                          |
| --------- | --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| TFM (lib) | `net9.0` + `net10.0`                    | .NET 9 is GA and the current LTS path; .NET 10 GA (Nov 2025) is also LTS. As of 2026-05-14 both are GA. Multi-target keeps .NET 9 consumers from being kicked off |
| TFM (test/sample) | `net10.0` only                  | Tests don't ship; pin the higher TFM for richer BCL surfaces and AOT-readiness checks                                                                        |
| OS        | `ubuntu-latest` + `windows-latest`      | The two OSes that matter for an SDK consumed in both server and developer-machine contexts. macOS is **optional** — informational, not gating                |
| Coverage  | 87% lines AND 87% branches              | Hard gate; PRs that drop the floor fail the merge                                                                                                            |
| Mutation  | nightly Stryker.NET                     | Informational at first; baseline kill-score recorded in Phase 3                                                                                              |

## 7. Coverage exclusions

A `coverlet.runsettings` file lives at repo root:

```xml
<DataCollectionRunSettings>
  <DataCollectors>
    <DataCollector friendlyName="XPlat code coverage">
      <Configuration>
        <Exclude>
          [*]*.g.cs,
          [*]*.JsonSerializerContext,
          [*]*Program,
          [ARCP.Cli]*
        </Exclude>
        <ExcludeByAttribute>
          GeneratedCodeAttribute,ExcludeFromCodeCoverageAttribute,CompilerGeneratedAttribute
        </ExcludeByAttribute>
      </Configuration>
    </DataCollector>
  </DataCollectors>
</DataCollectionRunSettings>
```

Excluded:

- `*.g.cs` — source-generator output, including the
  `JsonSerializerContext`. We test the **public surface** of the
  envelope round-trip (Layer 1), which exercises every generator-
  emitted path indirectly; testing the generator's emitted code is
  the wrong unit of work.
- `Program.cs` mains under `samples/` — sample bootstraps, not part
  of the SDK contract. Sample correctness is checked by Phase 6 (each
  sample exits 0).
- `[GeneratedCode]`-tagged types.
- The CLI `Main` shim in `src/ARCP.Cli/` — interactive surface
  exercised by the CLI's own tests, not by SDK coverage.

**Hand-written code is never excluded.** `[ExcludeFromCodeCoverage]`
attributes are budgeted to a soft cap of **10 types** repo-wide; the
budget gets flagged in review if exceeded. Excluding generated code
is correct; excluding hand-written code is a code-smell deferral.

## 8. Verify snapshot policy

- Snapshots live at `tests/<project>/snapshots/*.verified.txt`,
  checked in.
- `*.received.txt` is gitignored; it's only produced on mismatch.
- Pretty-printed JSON for envelope snapshots — keeps diffs reviewable.
- Hash mode (`Verifier.UseHashedFileName`) for binary `result_chunk`
  base64 payloads — JSON diff on a 1 MB blob is not useful, and the
  hash catches drift just as well.
- Approval workflow: failing tests print the `diff <verified> <received>`
  invocation in the assertion message; CI fails on any mismatch; a
  human renames `.received.txt` → `.verified.txt` locally to accept.

## 9. Property tests

**Argument FOR FsCheck:** the three parsers — `AgentRef.Parse`
(§7.5), `BudgetAmount.Parse` (§9.6), `Ulid.Parse` (already in
`src/ARCP/Ids/`) — all have narrow grammars where round-trip is the
defining invariant. One property per parser:

```text
[Property]
public Property AgentRef_Roundtrip(string name, string version) =>
    (IsValidName(name) && IsValidVersion(version)).Implies(
        AgentRef.Parse($"{name}@{version}").ToString() == $"{name}@{version}");
```

**Argument AGAINST:** the spec grammars are short
(`[a-zA-Z0-9.+_-]+`), the corner-cases are enumerable, and a
`[Theory]` with 30 explicit rows is more readable. FsCheck adds a
dependency and a shrinking-cost when a property fails.

**Decision:** `FsCheck.Xunit` is added for the three parsers above
only. Everywhere else, example-based `[Theory]` rows win on
readability. The FsCheck NuGet sits in `Directory.Packages.props`
behind one project ref in `ARCP.UnitTests`. If maintenance pain
appears in the first month, the three properties collapse back to
30 `[InlineData]` rows.

## 10. Bench plan

`tests/ARCP.Benchmarks/` — `BenchmarkDotNet` console exe, **not on the
CI required path**. Run on demand; tracked as informational
regressions.

Benchmarks:

- **Envelope serialize/deserialize** — one `[Benchmark]` per message
  type, `[Params]` over size buckets (small `session.ping`, medium
  `job.event`, large `result_chunk` 1 MB). Compares the
  source-generated `JsonSerializerContext` path against a `JsonSerializerOptions`
  reflection path so that, if the source-gen ever regresses, the
  bench notices.
- **`EventLog` append + read** — append-N-then-read-N microbench;
  exercises the session-scoped `event_seq` counter and the trim
  path that §6.5 uses.
- **Channel throughput at the §6.5 ack boundary** — `Channel<Envelope>`
  bounded with `BoundedChannelFullMode.Wait`, producer/consumer split
  across threads; measures the steady-state ack/event rate before the
  back-pressure threshold trips.

Off-thread by default (`[Params]`-driven); single-shot mode for
diagnostic runs. Results live in the repo `bench/` directory by
date; the bench project is informational only — never gating, never
in the merge path.
