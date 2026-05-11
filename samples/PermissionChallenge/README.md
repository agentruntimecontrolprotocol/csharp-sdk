# PermissionChallenge

Two-agent loop. Generator proposes patches; reviewer holds veto on
the `apply_patch` step via `permission.request`. Denied patches feed
back into the generator with the reviewer's reason. Bounded retry.

## Before ARCP

Either (a) the reviewer is a post-hoc filter that cannot say no with
authority — the generator already moved on; or (b) a custom
agent-to-agent RPC the two sides reimplement and re-bug every
quarter.

## With ARCP

```csharp
// generator side
LeaseId lease = await RequestApplyAsync(generator, ticketId, patch);

// reviewer side
await foreach (Env env in Events(reviewer))
{
    if (env.Type == "permission.request")
    {
        ReviewVerdict verdict = await Agents.ReviewAsync(ticket, env);
        await RespondAsync(reviewer, env, verdict);
    }
}
```

Two separate sessions. Same envelope contract. The reviewer's "no"
arrives at the generator as a structured `PERMISSION_DENIED` with a
`reason` field, not a 403 with a stack trace.

## ARCP primitives

- Permission challenge — RFC §15.4.
- Lease materialization — §15.5 (`lease.granted`).
- Structured errors — §18.
- `idempotency_key` per (ticket, diff) — §6.4.

## File tour

- `Program.cs` — bounded loop. Two sessions, one process for the demo.
- `Agents.cs` — `Propose` + `Review` stubs.
- `Stubs.cs` — elided client helpers.

## Variations

- Three reviewers (security + style + correctness); runtime gates
  `permission.grant` until all three respond.
- Stream test runner output as `kind: text` between attempts.
- Promote denied patches into a learning corpus via `event.emit`.
