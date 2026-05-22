# Conformance

The C# SDK aims for 100% conformance with ARCP v1.1. Full
section-by-section coverage lives in
[`../CONFORMANCE.md`](../CONFORMANCE.md); this page is the docs mirror.

## v1.1 coverage

| Section | Status | Notes |
| ------- | ------ | ----- |
| §4 Transport | ✅ full | WebSocket, stdio, in-memory. |
| §5 Wire format | ✅ full | Envelope, `arcp="1.1"`, ULID IDs, `event_seq`, `trace_id`. |
| §5.1 Vendor extensions | ✅ full | Unknown fields preserved in `Envelope.Extensions`. |
| §6 Sessions | ✅ full | Hello, welcome, error, bye. |
| §6.1 Authentication | ✅ full | Bearer + `StaticBearerVerifier`. Custom `IBearerVerifier`. |
| §6.3 Resume | ✅ full | Token rotated every welcome; window-bounded replay; gap-free. |
| §6.4 Heartbeat | ✅ full | `session.ping` / `session.pong`; not counted in `event_seq`. |
| §6.5 Ack | ✅ full | `session.ack { last_processed_seq }`; back-pressure status event. |
| §6.6 List jobs | ✅ full | `session.list_jobs` / `session.jobs` paginated. |
| §6.7 Bye | ✅ full | `session.bye { reason }` |
| §7 Jobs | ✅ full | Submit, accepted, event, result, error, cancel. |
| §7.2 Idempotency | ✅ full | Configurable TTL; `DUPLICATE_KEY` on content mismatch. |
| §7.3 State machine | ✅ full | `pending → running → terminal`. |
| §7.4 Cancellation | ✅ full | Submitter-only; `CancellationToken` propagated to agent. |
| §7.5 Agent versions | ✅ full | `name@version` grammar; `AGENT_VERSION_NOT_AVAILABLE`. |
| §7.6 Subscribe | ✅ full | Cross-session observation; subscribers cannot cancel. |
| §8 Job events | ✅ full | All 10 reserved kinds + `x-vendor.*`. |
| §8.3 Sequence numbers | ✅ full | Session-scoped, strictly monotonic, gap-free. |
| §8.4 Result chunks | ✅ full | `result_chunk` + terminal `job.result { result_id, result_size }`. |
| §9 Leases | ✅ full | Immutable per-job, glob matching, reserved namespaces. |
| §9.4 Delegation subset | ✅ full | `LeaseManager.AssertSubset`. |
| §9.5 Lease expiry | ✅ full | `expires_at` watchdog; `LEASE_EXPIRED`. |
| §9.6 Cost budget | ✅ full | Per-currency counters; `BUDGET_EXHAUSTED`. |
| §9.7 Model use | ✅ full | `model.use` glob enforcement; `PERMISSION_DENIED`. |
| §9.8 Credentials | ✅ full | Issued at submit, revoked on terminal, redacted from non-submitters. |
| §10 Delegation | ✅ full | `delegate` event kind; trace inheritance. |
| §11 Trace propagation | ✅ full | W3C via `Arcp.Otel.WithTracing()`. |
| §12 Error taxonomy | ✅ full | All 15 canonical codes with retryable booleans. |
| §15 Vendor extensions | ✅ full | Round-trip via `Envelope.Extensions`. |

## v1.1 features

All v1.1 features are negotiated in `capabilities.features` and default
to on (`FeatureSet.AllFeatures`):

| Feature            | Section     | Status |
| ------------------ | ----------- | ------ |
| `heartbeat`        | §6.4        | ✅ full |
| `ack`              | §6.5        | ✅ full |
| `list_jobs`        | §6.6        | ✅ full |
| `subscribe`        | §7.6        | ✅ full |
| `agent_versions`   | §7.5        | ✅ full |
| `progress`         | §8.2.1      | ✅ full |
| `result_chunk`     | §8.4        | ✅ full |
| `lease_expires_at` | §9.5        | ✅ full |
| `cost.budget`      | §9.6        | ✅ full |

Opt out of features on either peer:

```csharp
new ArcpClientOptions
{
    Features = new FeatureSet(["heartbeat", "ack"]), // drop the rest
};
```

## How conformance is tested

| Suite | Location | Facts |
| ----- | -------- | ----- |
| Unit | `tests/Arcp.UnitTests/` | 33 |
| Integration | `tests/Arcp.IntegrationTests/` | 12 |
| Conformance | `tests/Arcp.ConformanceTests/` | 14 |
| AspNetCore | `tests/Arcp.AspNetCore.Tests/` | 1 |

```sh
dotnet test ARCP.slnx
```

## Reporting a deviation

If you find behavior that disagrees with the
[v1.1 spec](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md),
open an issue with:

- Section number.
- Observed vs. expected behavior.
- Minimum reproducer — two files, `Server.cs` / `Client.cs`, runnable
  with `dotnet run`.

Tag with `conformance` and we'll triage.
