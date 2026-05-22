# Leases

A lease (spec §9) is the authority an agent operates under. It is immutable
for the life of the job and is granted at submit time by the client.

## Declare a lease

```csharp
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.FsRead]    = new[] { "/workspace/**" },
    [LeaseNamespaces.FsWrite]   = new[] { "/workspace/src/**" },
    [LeaseNamespaces.NetFetch]  = new[] { "https://api.example.com/*" },
    [LeaseNamespaces.ToolCall]  = new[] { "search.*", "fetch.*" },
    [LeaseNamespaces.CostBudget]= new[] { "USD:5.00" },
    [LeaseNamespaces.ModelUse]  = new[] { "tier-fast/*" },
});

var handle = await client.SubmitAsync("research", leaseRequest: lease);
```

## Reserved namespaces (§9.2)

| Namespace          | Pattern grammar     | Semantics                              |
| ------------------ | ------------------- | -------------------------------------- |
| `fs.read`          | path glob           | Filesystem read.                       |
| `fs.write`         | path glob           | Filesystem write.                      |
| `net.fetch`        | URL glob            | Outbound HTTP/HTTPS.                   |
| `tool.call`        | tool-name glob      | Calling registered tools.              |
| `agent.delegate`   | agent-name glob     | Delegation.                            |
| `cost.budget`      | `currency:decimal`  | Cumulative cost ceiling (§9.6).        |
| `model.use`        | model-id glob       | Allowed upstream model set (§9.7).     |

Use the `LeaseNamespaces` static class for the namespace string constants.
Glob matching is case-sensitive; `**` matches zero or more path segments.

## Enforce a lease in an agent

```csharp
server.RegisterAgent("file-writer", async (ctx, ct) =>
{
    var lm = new LeaseManager(timeProvider);

    try
    {
        lm.AuthorizeOperation(
            ctx.Lease,
            ctx.LeaseConstraints,
            LeaseNamespaces.FsWrite,
            path: "/workspace/src/output.cs");
    }
    catch (PermissionDeniedException ex)
    {
        await ctx.ToolResultAsync(callId, null,
            new ToolError { Code = ex.Code, Message = ex.Message }, ct);
        return null;
    }
    // perform the write …
    return new { wrote = "/workspace/src/output.cs" };
});
```

## Enforce model use (§9.7)

```csharp
lm.AuthorizeModelUse(ctx.Lease, ctx.LeaseConstraints, modelId: "tier-fast/gpt-4o-mini");
// throws PermissionDeniedException(PERMISSION_DENIED) on mismatch
```

## Subset for delegation (§9.4)

Child leases MUST be a subset of the parent's in every namespace and in every
budget ceiling. `LeaseManager.AssertSubset` validates this in one pass:

```csharp
var parentLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.ModelUse]  = new[] { "tier-fast/*" },
    [LeaseNamespaces.CostBudget]= new[] { "USD:2.00" },
});
var childLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.ModelUse]  = new[] { "tier-fast/gpt-4o-mini" },
    [LeaseNamespaces.CostBudget]= new[] { "USD:0.50" },
});

leaseManager.AssertSubset(parentLease, childLease);
// throws LeaseSubsetViolationException if any namespace is not covered
```

## Time-bounded leases (§9.5)

Set `ExpiresAt` in `LeaseConstraints` to bound the job's authority to a
deadline:

```csharp
var handle = await client.SubmitAsync(
    "research",
    leaseRequest: lease,
    leaseConstraints: new LeaseConstraints
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
    });
```

The runtime starts a watchdog that cancels the agent's `CancellationToken`
at `ExpiresAt` and emits `job.error { code: "LEASE_EXPIRED" }`. Requires the
`lease_expires_at` feature to be negotiated.

## Cost budget (§9.6)

See [Jobs — cost budget](./jobs.md#cost-budget-96) for reporting spend via
`ctx.MetricAsync` and handling `BUDGET_EXHAUSTED`.

## Related guides

- [Jobs](./jobs.md) — submit, cancel, idempotency.
- [Delegation](./delegation.md) — child-job lease subset.
- [Errors](./errors.md) — `PERMISSION_DENIED`, `LEASE_EXPIRED`, `BUDGET_EXHAUSTED`.
