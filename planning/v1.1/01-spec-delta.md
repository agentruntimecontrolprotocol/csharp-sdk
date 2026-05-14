# 01 — ARCP v1.1 Spec Delta

The v1.1 draft (`../spec/docs/draft-arcp-02.1.md`) is a **backward-compatible
additive revision** of v1.0. The envelope `arcp` field stays `"1"`; new
messages, fields, event kinds, lease constraints, and error codes are
guarded by a feature-flag handshake. A v1.0 client connecting to a
v1.1 runtime keeps working; a v1.1 client connecting to a v1.0
runtime degrades through the intersection of `session.hello.features`
and `session.welcome.features`.

Sections below cite spec §, classify additive vs. observably-breaking
for a current C# runtime/client, and pin the C# touch-point.

## 1. Capability negotiation (§6.2)

| Item                                                                                                                | Conf.  | Additive?  | C# touch-point                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | --------------------------------------------------------------------------------------- |
| `session.hello.payload.capabilities.features: string[]`                                                             | MUST   | additive   | `Messages/Session/Capabilities.cs` — add `Features` property                            |
| `session.welcome.payload.capabilities.features: string[]`                                                           | MUST   | additive   | same record; welcome currently called `session.accepted` in this SDK (see audit)        |
| Effective set is `intersect(hello.features, welcome.features)`                                                      | MUST   | additive   | new `Arcp.Core.FeatureSet` static — single intersection helper, no DI                   |
| `welcome.capabilities.agents` MAY be rich `{name, versions[], default?}[]` shape                                    | MAY    | additive   | `Capabilities.Agents` becomes a discriminated union — `JsonConverter` over `JsonElement` |
| Either peer MUST NOT use a feature outside the intersection                                                         | MUST   | additive   | client-side guard throws `INVALID_REQUEST` `ArcpException`                              |

Canonical v1.1 feature names: `heartbeat`, `ack`, `list_jobs`,
`subscribe`, `lease_expires_at`, `cost.budget`, `progress`,
`result_chunk`, `agent_versions`. The runtime advertises only what
it can actually serve; clients advertise only what they implement.

## 2. Heartbeats — §6.4 (`heartbeat`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `session.ping { nonce, sent_at }` / `session.pong { ping_nonce, received_at }`                                      | MUST   | additive   | new records under `Messages/Session/`                                          |
| `welcome.payload.heartbeat_interval_sec` advertised by runtime                                                      | MUST   | additive   | extend `SessionAccepted` (current name) → `SessionWelcome` payload             |
| Idle peer SHOULD send ping each interval; responder MUST reply within interval                                      | SHOULD | additive   | `PeriodicTimer` task per `SessionState`, ConfigureAwait(false)                 |
| Two consecutive silent intervals MAY close transport with `HEARTBEAT_LOST`                                          | MAY    | additive   | already-present `HEARTBEAT_LOST` code is repurposed                            |
| Ping/pong NOT counted in `event_seq`                                                                                | MUST   | additive   | exclude from `EventLog.append`                                                 |
| Runtime MUST NOT terminate jobs on heartbeat loss; session remains for resume window                                | MUST   | additive   | `JobManager` is unaware of session liveness                                    |

## 3. Event acknowledgement — §6.5 (`ack`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `session.ack { last_processed_seq }`                                                                                | MAY    | additive   | new `SessionAck` record                                                        |
| Runtime MAY free buffered events ≤ `last_processed_seq` early                                                       | MAY    | additive   | `EventLog.TrimBeforeAsync(long seq, CT)`                                       |
| Runtime MUST NOT free unacked events even past the time window (memory caps allowed)                                | MUST   | additive   | the trim is *advisory*; resume buffer is the authority                         |
| Runtime MAY emit `status { phase: "back_pressure" }` when lag exceeds threshold                                     | MAY    | additive   | `Channel<Envelope>` bounded; lag = highWatermark − lastAck                     |
| `session.ack` NOT counted in `event_seq`                                                                            | MUST   | additive   | exclude from sequence                                                          |

## 4. Job listing — §6.6 (`list_jobs`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `session.list_jobs { filter?, limit?, cursor? }` → `session.jobs { request_id, jobs[], next_cursor? }`              | MAY    | additive   | request/response pair; correlate via envelope `id`                             |
| `JobListEntry { job_id, agent, status, lease, parent_job_id?, created_at, trace_id?, last_event_seq }`              | MUST   | additive   | sealed `record` with `init`-only props                                         |
| Filters: `status?`, `agent?`, `created_after?` (ISO 8601 UTC)                                                       | MAY    | additive   | all optional                                                                   |
| Scope: same-principal by default; deployment policy may broaden                                                     | MUST   | additive   | `IJobAuthorizationPolicy` extension point, default `SamePrincipalPolicy`       |
| Read-only: subscribing requires `job.subscribe` (§7.6)                                                              | MUST   | additive   | listing emits no events                                                        |

## 5. Agent versioning — §7.5 (`agent_versions`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `agent ::= name \| name "@" version`; `version` = `[a-zA-Z0-9.+_-]+`                                                | MAY    | additive   | new `AgentRef` `record struct` with `Parse` / `ToString`                       |
| Bare name → `default` version from inventory (else any registered version)                                          | MUST   | additive   | `IAgent` registry by `(name, version)`                                         |
| Pinned `name@version` not registered → `AGENT_VERSION_NOT_AVAILABLE`                                                | MUST   | additive   | new exception subclass                                                         |
| `job.accepted.payload.agent` echoes resolved `name@version`                                                         | MUST   | additive   | `JobAccepted.Agent` is `AgentRef`, not string                                  |
| Running job's resolved version is fixed; runtime MUST NOT migrate                                                   | MUST   | additive   | `Job.AgentVersion` is `init` only                                              |

## 6. Job subscription — §7.6 (`subscribe`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `job.subscribe { job_id, from_event_seq?, history? }`                                                               | MAY    | additive   | distinct from `subscription.*` already present in this SDK (rename audit item) |
| `job.subscribed { current_status, agent, lease, parent_job_id?, trace_id?, subscribed_from, replayed }`             | MUST   | additive   | response carrying snapshot                                                     |
| `job.unsubscribe { job_id }`                                                                                        | MAY    | additive   | one-way                                                                        |
| Auth: same principal default; broader via deployment policy; `PERMISSION_DENIED` on refusal                         | MUST   | additive   | reuses §6.6 `IJobAuthorizationPolicy`                                          |
| Subscription does NOT confer cancel authority                                                                       | MUST   | additive   | `JobManager.Cancel` checks submitter identity                                  |
| `history: true` replays buffered events; each replayed event uses the **subscriber's** session-scoped `event_seq`   | MUST   | additive   | requires per-session seq counter — already present                             |

## 7. Progress events — §8.2.1 (no flag; emit only when negotiated)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| New event `kind: "progress"`, body `{ current, total?, units?, message? }`                                          | MUST   | additive   | sealed `record ProgressBody`; reserved kind list grows by one                  |
| `current ≥ 0`; if `total` present, `current ≤ total` SHOULD                                                         | MUST   | additive   | constructor validation via guard, throws `INVALID_REQUEST`                     |
| Protocol does not act on `progress`                                                                                 | MUST   | additive   | advisory                                                                       |

## 8. Result streaming — §8.4 (`result_chunk`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| New event `kind: "result_chunk"`, body `{ result_id, chunk_seq, data, encoding ∈ {utf8,base64}, more }`             | MUST   | additive   | `ResultChunkBody` with `EncodedString` discriminator                           |
| `chunk_seq` 0-based monotonic per `result_id`                                                                       | MUST   | additive   | runtime asserts                                                                |
| Terminal `job.result.payload` carries `result_id`, `result_size`, `summary?`                                        | MUST   | additive   | `JobResult` gets nullable `ResultId`/`ResultSize`                              |
| MUST NOT mix inline `result` and `result_chunk` in same job                                                         | MUST   | additive   | server-side invariant; throw `InvalidRequestException`                         |
| Client surface: `IAsyncEnumerable<ResultChunk>` consumed via `await foreach`                                        | —      | additive   | `JobHandle.Chunks(CT)` — see Phase 4                                           |

## 9. Lease expiration — §9.5 (`lease_expires_at`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `job.submit.payload.lease_constraints.expires_at` (ISO 8601 UTC `Z`, future)                                        | MAY    | additive   | `DateTimeOffset` parsed; rejects non-UTC and past                              |
| `job.accepted.payload.lease_constraints` echoes effective constraints                                               | MUST   | additive   | new record `LeaseConstraints`                                                  |
| Operations at/after `expires_at` MUST fail with `LEASE_EXPIRED`                                                     | MUST   | additive   | `LeaseManager.Authorize(...)` checks `TimeProvider.GetUtcNow()`                |
| Runtime MUST emit `job.error { final_status: "error", code: "LEASE_EXPIRED" }` if still running at expiry           | MUST   | additive   | `PeriodicTimer` watchdog rooted on the job's `CancellationTokenSource`         |
| Renewal NOT supported in v1.1                                                                                       | MUST   | additive   | no API; new lease requires resubmit                                            |

## 10. Budget capability — §9.6 (`cost.budget`)

| Item                                                                                                                | Conf.  | Additive?  | Notes                                                                          |
| ------------------------------------------------------------------------------------------------------------------- | ------ | ---------- | ------------------------------------------------------------------------------ |
| `cost.budget` reserved capability; amount grammar `currency:decimal` (`USD`/`EUR`/`credits`/custom)                 | MAY    | additive   | `BudgetAmount` parser; uses `decimal`, not `double`                            |
| Per-currency counters initialized at `job.accepted`                                                                  | MUST   | additive   | `Dictionary<string,decimal>` echoed in `job.accepted.payload.budget`           |
| `metric { name: "cost.*", unit: <currency>, value }` decrements counter                                             | MUST   | additive   | `JobManager.ApplyMetric` intercept; negative `value` → reject silently         |
| Operations MUST check budget; counter ≤ 0 → `BUDGET_EXHAUSTED`                                                      | MUST   | additive   | preferred surface: `tool_result.error`; `job.error` only if fatal               |
| Runtime MAY emit `cost.budget.remaining` metric post-decrement                                                      | MAY    | additive   | debounce by 5 % rule to match TS                                               |
| Sub-lease budget MUST NOT exceed parent remaining (§9.4 add'n)                                                      | MUST   | additive   | `LeaseManager.AssertSubset` extended with budget-remaining input               |

## 11. Lease subsetting additions — §9.4

- Child `lease_constraints.expires_at` MUST NOT exceed parent's (MUST).
- Child without `lease_constraints` inherits parent's expiry implicitly (MUST).
- Both additive.

## 12. Error taxonomy — §12

v1.1 adds three codes, total 15 canonical:

| Code                          | Status | Effect on C# `ErrorCode` enum                          |
| ----------------------------- | ------ | ------------------------------------------------------ |
| `AGENT_VERSION_NOT_AVAILABLE` | new    | new enum member + exception subclass                   |
| `LEASE_EXPIRED`               | new    | enum member already present (audit) — repurpose        |
| `BUDGET_EXHAUSTED`            | new    | new enum member + exception subclass                   |

All three are `retryable: false`. The base taxonomy that must
already exist (12 codes) is the v1.0 set: `PERMISSION_DENIED`,
`LEASE_SUBSET_VIOLATION`, `JOB_NOT_FOUND`, `DUPLICATE_KEY`,
`AGENT_NOT_AVAILABLE`, `CANCELLED`, `TIMEOUT`, `RESUME_WINDOW_EXPIRED`,
`HEARTBEAT_LOST`, `INVALID_REQUEST`, `UNAUTHENTICATED`,
`INTERNAL_ERROR`. The current SDK's `ErrorCode` enum (21 members)
does not match this set — see Phase 2.

## 13. Trace propagation additions — §11

Recommended OTel span attributes:

- `arcp.lease.expires_at` — ISO 8601 string.
- `arcp.budget.remaining` — JSON-stringified `{currency: decimal}` map.

Both go into the v1.1 OTel adapter (`Arcp.Otel`, Phase 5).

## 14. Observably-breaking changes

There are none for a v1.0 *protocol* client. Every v1.1 message,
field, and error code is gated by feature negotiation. The
"observably-breaking" surface in this SDK is internal: the C# code
is not currently on the v1.0 wire (see Phase 2), so the migration's
**first** PR-sized step is to land v1.0 conformance, after which v1.1
is genuinely additive.

## 15. Anti-deferral checklist

Three items the TS reference ships and v1.1 does NOT defer; the C#
plan must not quietly drop them:

- `JobAuthorizationPolicy` extension point shared by `list_jobs` and
  `subscribe`. Defaulting to "same principal" is required.
- `result_chunk` size cap (per §14 security): runtimes SHOULD cap
  individual chunks (e.g., 1 MB); exceeding either bound MUST
  surface `INTERNAL_ERROR`.
- Heartbeat clock discipline (§14): runtime SHOULD evaluate
  intervals against `TimeProvider` (test injection) rather than
  `DateTime.UtcNow` directly.

## 16. Out of scope for v1.1

Per the spec's "Not in v1.1": job pause/unpause, priority &
scheduling hints, federation across runtimes, streaming-token surface
for LLM outputs. Any current C# code that anticipates these
(e.g., `Messages/Streaming`, `Priority.cs`) is **not** how v1.1 is
expected to model them — see Phase 2.
