# ARCP v1.1 Conformance

This document maps each spec § to the C# surface and test that demonstrates it. Tests live in [`tests/Arcp.ConformanceTests/`](./tests/Arcp.ConformanceTests/SpecConformanceTests.cs) and [`tests/Arcp.IntegrationTests/`](./tests/Arcp.IntegrationTests/EndToEndTests.cs).

Status legend: Implemented ✅ · Partial 🚧 · Not implemented ⛔.

## Wire format

| Spec | Requirement | Status | Where |
| ---- | ----------- | ------ | ----- |
| §4.1 | WebSocket text frames, `/arcp` path | ✅ | `WebSocketTransport`, `Arcp.AspNetCore.MapArcp` |
| §4.2 | stdio newline-delimited JSON | ✅ | `StdioTransport` |
| §5.1 | Envelope: `arcp="1.1"`, `id`, `type`, optional `session_id`/`trace_id`/`job_id`/`event_seq`, `payload` | ✅ | `Envelope`, `EnvelopeJsonConverter` |
| §5.1 | Unknown top-level fields MUST be ignored | ✅ | `Envelope.Extensions` |

## Sessions

| Spec | Requirement | Status | Where |
| ---- | ----------- | ------ | ----- |
| §6.1 | Bearer token in `session.hello.payload.auth.token` | ✅ | `IBearerVerifier`, `StaticBearerVerifier` |
| §6.2 | `session.hello` ↔ `session.welcome` capability exchange | ✅ | `SessionState.HandleHelloAsync` |
| §6.2 | Effective features = intersect(hello.features, welcome.features) | ✅ | `FeatureSet.Intersect` |
| §6.2 | `welcome.capabilities.agents` rich `{name, versions[], default?}` shape | ✅ | `AgentInventoryEntry` |
| §6.3 | Resume token rotated every welcome | ✅ | `SessionState.MintResumeToken` |
| §6.4 | `session.ping` / `session.pong`; ping/pong NOT in `event_seq` | ✅ | `SessionState.HeartbeatLoop` |
| §6.5 | `session.ack { last_processed_seq }`; back-pressure status event when lag is high | ✅ | `SessionState` ack handler |
| §6.6 | `session.list_jobs` / `session.jobs` paginated | ✅ | `JobManager.List`, `ArcpClient.ListJobsAsync` |
| §6.7 | `session.bye { reason }` close | ✅ | `SessionState.CloseAsync` |

## Jobs

| Spec | Requirement | Status | Where |
| ---- | ----------- | ------ | ----- |
| §7.1 | `job.submit` payload | ✅ | `JobSubmitPayload` |
| §7.1 | `job.accepted` carries effective lease + budget + accepted_at | ✅ | `JobAcceptedPayload`, `JobManager.Submit` |
| §7.2 | Idempotency via `payload.idempotency_key` | ✅ | `JobManager._idempotency` |
| §7.3 | Lifecycle: `pending → running → {success | error | cancelled | timed_out}` | ✅ | `JobStatus`, `JobManager.RunAsync` |
| §7.4 | `job.cancel` → `job.error { final_status: "cancelled" }` | ✅ | `JobManager.Cancel`, `RunAsync` |
| §7.5 | `agent ::= name | name@version`; `AGENT_VERSION_NOT_AVAILABLE` for unknown pin | ✅ | `AgentRef`, `AgentRegistry` |
| §7.6 | `job.subscribe` / `job.subscribed` / `job.unsubscribe`; subscribers cannot cancel | ✅ | `SubscriptionManager`, `ArcpClient.SubscribeAsync` |

## Events

| Spec | Requirement | Status | Where |
| ---- | ----------- | ------ | ----- |
| §8.1 | One `job.event` envelope discriminated on `payload.kind` | ✅ | `JobEventPayload` |
| §8.2 | Reserved kinds (10) | ✅ | `EventKinds` |
| §8.2.1 | `progress` body: `current ≥ 0`, `current ≤ total` if set | ✅ | `ProgressBody.Validate` |
| §8.3 | `event_seq` session-scoped, monotonic, gap-free | ✅ | `EventLog.Append` |
| §8.4 | `result_chunk` event + terminal `job.result { result_id, result_size, summary? }` | ✅ | `ResultChunkBody`, `Job.WriteChunkAsync` |
| §8.4 | MUST NOT mix inline result and `result_chunk` | ✅ | `Job.BeginResultStream` guard |

## Leases

| Spec | Requirement | Status | Where |
| ---- | ----------- | ------ | ----- |
| §9.1 | Lease immutable, granted at submit | ✅ | `Lease`, `JobAcceptedPayload` |
| §9.2 | Reserved namespaces | ✅ | `LeaseNamespaces` |
| §9.3 | Out-of-lease operations produce `PERMISSION_DENIED` | ✅ | `LeaseManager.AuthorizeOperation` |
| §9.4 | Subset validation for delegation | ✅ | `LeaseManager.AssertSubset` |
| §9.5 | `lease_constraints.expires_at` UTC + future-only; watchdog emits `LEASE_EXPIRED` | ✅ | `LeaseManager.Authorize`, `JobManager.RunLeaseWatchdog` |
| §9.6 | `cost.budget` per-currency counters; `cost.*` metrics decrement | ✅ | `BudgetLedger`, `Job.EmitMetricAsync` |

## Delegation, tracing, errors

| Spec | Requirement | Status | Where |
| ---- | ----------- | ------ | ----- |
| §10  | `delegate` event kind on parent's `job.event` stream | ✅ | `JobContext.DelegateAsync` |
| §11  | `trace_id` propagation; OTel span attrs | ✅ | `TraceAttributes`, `ArcpTracing.WithTracing` |
| §11 (v1.1) | Span attrs `arcp.lease.expires_at`, `arcp.budget.remaining` | ✅ | `TraceAttributes` |
| §12  | 15 canonical error codes with retryable booleans | ✅ | `ErrorCode.All`, `ErrorCode.IsRetryable` |

## Test cross-reference

- Unit: [`tests/Arcp.UnitTests/`](./tests/Arcp.UnitTests/) — 22 facts.
- End-to-end: [`tests/Arcp.IntegrationTests/`](./tests/Arcp.IntegrationTests/) — 6 facts.
- Conformance: [`tests/Arcp.ConformanceTests/`](./tests/Arcp.ConformanceTests/) — 12 facts.
- AspNetCore: [`tests/Arcp.AspNetCore.Tests/`](./tests/Arcp.AspNetCore.Tests/) — 1 fact.

Run `dotnet test ARCP.slnx` to execute all 41.
