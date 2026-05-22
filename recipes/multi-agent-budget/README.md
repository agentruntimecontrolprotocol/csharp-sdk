# Recipe: multi-agent-budget

A planner agent splits a research topic into sub-questions and dispatches each
to a worker agent under a strict USD budget cap.

## What this demonstrates

| Feature | Spec ref |
| ------- | -------- |
| Budget cascade via child leases | §13.2 |
| `ctx.DelegateAsync` — recording delegation in the event stream | §13 |
| `ctx.MetricAsync` — debiting the parent's own budget after each grant | §9.6 |
| `ctx.Budget` — reading remaining funds mid-handler | §9.6 |
| Multi-session fan-out using `MemoryTransport.Pair()` | §4 |

## The "debit-self-for-each-grant" pattern

The planner calls `ctx.MetricAsync("cost.delegate", sliceUsd, "USD")` immediately
after each delegation.  The next loop iteration checks `ctx.Budget["USD"]` against
the next slice cost; because the debit was already applied, the planner cannot
over-commit funds regardless of when child jobs actually execute.

```
USD 5.00 budget
  └─ question 0: grant $2.00, debit planner → $3.00 remaining
  └─ question 1: grant $2.00, debit planner → $1.00 remaining
  └─ question 2: $1.00 < $2.00 → SKIPPED (logged as "budget cap reached")
```

## Run

```sh
dotnet run --project recipes/multi-agent-budget
```

## Related

- [Budget cascade guide](../../docs/guides/leases.md#budget-cascade)
- [Delegation guide](../../docs/guides/delegation.md)
- [`samples/CostBudget`](../../samples/CostBudget/) — single-agent budget basics
- [`samples/Delegate`](../../samples/Delegate/) — `ctx.DelegateAsync` in isolation
