# Delegation

An agent delegates work by submitting a child job and emitting a `delegate`
event on its own stream linking the two (spec §10). The child's lease MUST be
a subset of the parent's (spec §9.4).

## Basic pattern

```csharp
server.RegisterAgent("orchestrator", async (ctx, ct) =>
{
    // Build a child lease that is a subset of ctx.Lease:
    var childLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
    {
        [LeaseNamespaces.NetFetch]  = new[] { "https://*.example.com/*" },
        [LeaseNamespaces.CostBudget]= new[] { "USD:0.50" },
    });
    leaseManager.AssertSubset(ctx.Lease, childLease);

    // Submit on a separate child client:
    var child = await childClient.SubmitAsync(
        agent: "research",
        input: new { topic = "arcp" },
        leaseRequest: childLease);

    // Link the child to this job's stream:
    await ctx.DelegateAsync(child.JobId.Value, "research",
        input: new { topic = "arcp" }, ct);

    var summary = await child.Result;
    return new { summary = summary.Output };
});
```

`ctx.DelegateAsync` emits a `delegate` job event so observers and the trace
backend know about the parent → child link.

## Lease subset enforcement

`LeaseManager.AssertSubset` validates capability namespaces, `expires_at`
bounds, and per-currency budget ceilings in one call:

```csharp
// throws LeaseSubsetViolationException if child is not covered by parent
leaseManager.AssertSubset(parentLease, childLease);
```

A child's `cost.budget` ceiling MUST NOT exceed the parent's remaining budget
in any currency. The runtime enforces this at child-submit time; pre-validating
with `AssertSubset` in the agent gives a cleaner error before the round trip.

## Cancellation

Cancelling the parent does **not** automatically cancel children — they are
independent jobs with their own lease and submitter. If you need coordinated
cancellation, link `CancellationToken`s manually:

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
var child = await childClient.SubmitAsync("research", ..., cancellationToken: cts.Token);
// If parent agent is cancelled, ct fires, cts fires, child is cancelled too.
```

## Trace propagation (§11)

Child jobs SHOULD reuse the parent's W3C trace context so spans link in
any OTel backend. The SDK reads the ambient `Activity.Current` when
emitting envelopes, so run the child submit inside an activity started
from the parent's trace ID:

```csharp
using var activity = ArcpDiagnostics.Runtime.StartActivity("delegate.research");
// Activity.Current now carries ctx.TraceId-derived context; child envelopes
// inherit it via the OTel transport wrapper.
var child = await childClient.SubmitAsync("research", input: new { topic });
```

See [Observability](./observability.md) for the full trace setup.

## Related guides

- [Leases](./leases.md) — subset enforcement, `AssertSubset`.
- [Jobs](./jobs.md) — `SubmitAsync`, budget.
- [Observability](./observability.md) — trace propagation.
