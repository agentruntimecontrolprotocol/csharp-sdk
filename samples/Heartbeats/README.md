# Heartbeats

Dynamic peer-runtime federation. Workers register, take work via
`agent.delegate`, send heartbeats, and deregister cleanly. Heartbeat
loss reroutes the in-flight task to another worker — deduped by
`idempotency_key`.

## Before ARCP

Static worker pools with bespoke RPCs. The supervisor's "is this
worker alive?" answer comes from a TCP keepalive (lies during GC) or
a custom heartbeat that re-dispatch logic doesn't actually trust.

## With ARCP

```csharp
foreach (Worker w in roster.Workers.Values.ToList())
{
    if ((DateTimeOffset.UtcNow - w.LastHeartbeat).TotalSeconds <= DeadlineSeconds) continue;
    if (w.InFlightJob is { } jid && jobsToTasks.Remove(jid, out WorkTask? task))
    {
        await DispatchAsync(client, task, roster, jobsToTasks); // same idempotency_key
    }
    roster.Remove(w);
}
```

`idempotency_key` makes re-dispatch safe: a worker that survived the
network blip will see the duplicate `agent.delegate` and dedupe.

## ARCP primitives

- Capability negotiation (per-role extension) — RFC §7, §21.
- `agent.delegate` — §14.
- Job lifecycle (accepted → started → heartbeat → terminal) — §10.
- Heartbeat loss recovery — §10.3 (`heartbeat_recovery: "block"`).
- `idempotency_key` for safe re-dispatch — §6.4.
- Trust levels — §15.3.

## File tour

- `Program.cs` — boots supervisor + small worker pool in-process.
- `Roster.cs` — capability index + freshness bookkeeping.
- `Work.cs` — actual work (stubbed).
- `Stubs.cs` — elided client helpers.

## Variations

- Priority queues by tagging tasks with envelope `priority`.
- Per-worker quota tracked via `tokens.used` metrics.
- Replace the in-process workers with separate processes; the
  protocol shape doesn't change.
