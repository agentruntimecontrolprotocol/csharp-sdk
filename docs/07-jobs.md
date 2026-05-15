---
title: Jobs
sdk: csharp
spec_sections: ["§7.1", "§7.2", "§7.3", "§7.4"]
order: 7
kind: reference
---

## Submit (§7.1)

`job.submit` carries the agent reference, input, optional lease, optional `lease_constraints`, optional `idempotency_key`, and optional `max_runtime_sec`.

```csharp
JobHandle handle = await client.SubmitAsync(
    agent: "code-refactor@2.0.0",
    input: new { repo = "/work" },
    leaseRequest: lease,
    leaseConstraints: new LeaseConstraints { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
    idempotencyKey: "key-2026-W19");
```

The returned `JobHandle` is populated once `job.accepted` arrives. `handle.JobId`, `handle.Agent`, `handle.Lease`, `handle.Budget`, and `handle.TraceId` are all set by then.

## Idempotency (§7.2)

Submitting twice with the same `idempotencyKey` and principal returns the same `job_id` and resolves to the same job. A submit with the same key but different `agent` or `input` produces `DUPLICATE_KEY`.

## Lifecycle (§7.3)

```
pending → running → { success | error | cancelled | timed_out }
```

The terminal envelope is either `job.result` (success) or `job.error` (everything else). `JobResult.Success` distinguishes the two on the client side.

## Cancellation (§7.4)

```csharp
await handle.CancelAsync(reason: "user-requested");
var result = await handle.Result;
// result.FinalStatus == "cancelled"
```

The runtime signals cancellation by cancelling the agent's `CancellationToken`. Agents should propagate it; the runtime emits `job.error { final_status: "cancelled", code: "CANCELLED" }` once the agent unwinds.

## Submitter authority

Only the submitter session may cancel the job. Subscribers from other sessions are observation-only (spec §7.6).
