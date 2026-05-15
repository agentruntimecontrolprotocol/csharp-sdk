---
title: Leases
sdk: csharp
spec_sections: ["§9.1", "§9.2", "§9.3", "§9.4"]
order: 9
kind: reference
---

A lease (spec §9) is the authority an agent operates under. It is immutable for the life of the job and granted at submit time.

```csharp
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    ["fs.read"]    = new[] { "/workspace/**" },
    ["fs.write"]   = new[] { "/workspace/src/**" },
    ["net.fetch"]  = new[] { "https://api.example.com/*" },
    ["tool.call"]  = new[] { "search.*", "fetch.*" },
    ["cost.budget"]= new[] { "USD:5.00" },
});
```

## Reserved namespaces (§9.2)

| Namespace        | Pattern grammar     | Semantics                          |
| ---------------- | ------------------- | ---------------------------------- |
| `fs.read`        | path glob           | Filesystem read.                   |
| `fs.write`       | path glob           | Filesystem write.                  |
| `net.fetch`      | URL glob            | Outbound HTTP/HTTPS.               |
| `tool.call`      | tool-name glob      | Calling registered tools.          |
| `agent.delegate` | agent-name glob     | Delegation.                        |
| `cost.budget`    | `currency:decimal`  | Cumulative cost ceiling. (§9.6)    |

Use the `LeaseNamespaces` static class for the namespace string constants.

## Enforcement (§9.3)

```csharp
var lm = new LeaseManager(timeProvider);
try
{
    lm.AuthorizeOperation(ctx.Lease, ctx.LeaseConstraints, LeaseNamespaces.FsRead, "/etc/passwd");
}
catch (PermissionDeniedException ex)
{
    await ctx.ToolResultAsync(callId, null, new ToolError { Code = ex.Code, Message = ex.Message });
}
```

## Subset for delegation (§9.4)

Child leases MUST be a subset of the parent's. The `LeaseManager.AssertSubset` method validates capability namespaces, expires_at bounds, and per-currency budget ceilings in one pass.

## Time-bounded leases (§9.5) and budget (§9.6)

See [`15-budget.md`](./15-budget.md) and the runtime watchdog described in [`04-runtime.md`](./04-runtime.md).
