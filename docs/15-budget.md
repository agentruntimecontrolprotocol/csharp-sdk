---
title: Cost budgets
sdk: csharp
spec_sections: ["§9.6"]
order: 15
kind: guide
---

The `cost.budget` capability (spec §9.6) declares an upper bound on cumulative cost per currency. The runtime tracks per-currency counters in a `BudgetLedger` and refuses authority-bearing operations when a counter hits zero.

## Declare a budget

```csharp
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    ["cost.budget"] = new[] { "USD:5.00", "credits:1000" },
});
var handle = await client.SubmitAsync("research", leaseRequest: lease);
```

Counters are initialized from the budget amount strings at `job.accepted`. The amount grammar is `currency:decimal`. `decimal` (not `double`) is used end-to-end to avoid binary-float rounding.

## Report cost

The agent reports cost via `metric` events whose `name` starts with `cost.` and whose `unit` matches a budgeted currency:

```csharp
await ctx.MetricAsync("cost.inference", 0.0234, unit: "USD", cancellationToken: ct);
```

Negative values are rejected silently per spec §9.6.

The runtime MAY emit `cost.budget.remaining` metrics on material decrements so clients can render a gauge without summing every cost event.

## Exhaustion

When any counter goes ≤ 0, the next authority-bearing operation produces `BUDGET_EXHAUSTED`. The preferred surface is a `tool_result.error` so the agent can decide whether to continue with non-cost-bearing work. The runtime escalates to `job.error` only when exhaustion is fatal.

## Budget on delegation

A child lease's `cost.budget` MUST NOT exceed the parent's remaining budget in any currency (spec §9.4 addition). `LeaseManager.AssertSubset` enforces this.

See [`samples/CostBudget/`](../samples/CostBudget/).
