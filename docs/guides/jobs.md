# Jobs

A job is the unit of work managed by the runtime. The client submits one,
receives a `JobHandle`, observes events, and eventually reads a terminal
result.

## Submit (§7.1)

```csharp
JobHandle handle = await client.SubmitAsync(
    agent:            "code-refactor@2.0.0",
    input:            new { repo = "/workspace/app" },
    leaseRequest:     new Lease(new Dictionary<string, IReadOnlyList<string>>
    {
        [LeaseNamespaces.FsRead]    = new[] { "/workspace/**" },
        [LeaseNamespaces.FsWrite]   = new[] { "/workspace/src/**" },
        [LeaseNamespaces.CostBudget]= new[] { "USD:5.00" },
    }),
    leaseConstraints: new LeaseConstraints
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    },
    idempotencyKey:   "refactor-2026-W19",
    maxRuntimeSec:    3600);
```

`SubmitAsync` returns once `job.accepted` arrives. The handle is populated:

```csharp
Console.WriteLine(handle.JobId);     // "job_01J..."
Console.WriteLine(handle.Agent);     // "code-refactor@2.0.0"
Console.WriteLine(handle.TraceId);   // W3C trace ID
// handle.Lease, handle.Budget also available
```

## Lifecycle (§7.3)

```
pending → running → { success | error | cancelled | timed_out }
```

The terminal envelope is `job.result` for success and `job.error` for all
failure modes. `JobResult.FinalStatus` and `JobResult.Success` distinguish
them on the client side.

## Observe events

```csharp
await foreach (var ev in handle.Events(cancellationToken))
{
    switch (ev.Kind)
    {
        case "log":      Console.WriteLine(ev.Body.GetProperty("message").GetString()); break;
        case "progress": Console.WriteLine($"{ev.Body.GetProperty("current")}/{ev.Body.GetProperty("total")}"); break;
        default:         Console.WriteLine(ev.Kind); break;
    }
}
var result = await handle.Result;
// result.Success == true
// result.FinalStatus == "success"
// result.Result?.Result — the agent's return value deserialized
```

## Await result directly

If you don't need to watch events:

```csharp
var result = await handle.Result;
```

`handle.Result` completes when the terminal envelope (`job.result` or
`job.error`) arrives.

## Cancellation (§7.4)

```csharp
await handle.CancelAsync(reason: "user-requested");
var result = await handle.Result;
// result.FinalStatus == "cancelled"
```

Only the submitter session can cancel. The runtime signals cancellation by
cancelling the agent's `CancellationToken`. Agents should propagate it —
the runtime emits `job.error { final_status: "cancelled", code: "CANCELLED" }`
once the agent unwinds.

## Idempotency (§7.2)

Submitting twice with the same `idempotencyKey` and same principal returns
the same `job_id` and resolves to the same job:

```csharp
var h1 = await client.SubmitAsync("echo", new { hi = 1 }, idempotencyKey: "run-42");
var h2 = await client.SubmitAsync("echo", new { hi = 1 }, idempotencyKey: "run-42");
// h1.JobId == h2.JobId
```

Submitting the same key with a different `agent` or `input` produces
`DuplicateKeyException` (`DUPLICATE_KEY`).

TTL is configurable on the server:

```csharp
new ArcpServerOptions
{
    IdempotencyWindowSec = 86400,   // 24 h (default)
};
```

## Cost budget (§9.6)

Declare a budget ceiling in the lease:

```csharp
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.CostBudget] = new[] { "USD:5.00", "credits:1000" },
});
```

The agent reports spend via `metric` events:

```csharp
await ctx.MetricAsync("cost.inference", 0.0234, unit: "USD", cancellationToken: ct);
```

When any counter reaches zero the next authority-bearing call returns
`BUDGET_EXHAUSTED`. The runtime surfaces this as a `tool_result.error` so the
agent can decide whether to continue with non-cost-bearing work; it escalates
to `job.error` only when exhaustion is fatal.

Child leases on delegation MUST NOT exceed the parent's remaining budget.
`LeaseManager.AssertSubset` enforces this (see [Leases](./leases.md)).

## Streamed results (§8.4)

For large final results the agent emits a sequence of `result_chunk` events:

**Agent side:**

```csharp
server.RegisterAgent("report", async (ctx, ct) =>
{
    var rid = ctx.BeginResultStream();
    await ctx.WriteChunkAsync(rid, "Title\n",        more: true,  ct);
    await ctx.WriteChunkAsync(rid, "Section A: ...", more: true,  ct);
    await ctx.WriteChunkAsync(rid, "End.",           more: false, ct);
    return "report generated, 3 chunks";   // becomes terminal summary
});
```

**Client side:**

```csharp
var handle = await client.SubmitAsync("report");
var assembled = new StringBuilder();
await foreach (var chunk in handle.Chunks(cancellationToken))
    assembled.Append(chunk.DecodedString);

var terminal = await handle.Result;
// terminal.Result.ResultId   — ULID referencing the assembled content
// terminal.Result.ResultSize — total byte count
// terminal.Result.Summary    — the agent's return value
```

Binary chunks: pass a `ReadOnlyMemory<byte>` overload; the runtime
base64-encodes it for transport.

Invariants (spec §8.4):

- `chunk_seq` is 0-based monotonic per `result_id`.
- The final chunk has `more: false`.
- `encoding ∈ { "utf8", "base64" }`.
- A job MUST NOT mix inline `result` and `result_chunk`.

## Related guides

- [Job events](./job-events.md) — all 10 reserved event kinds.
- [Leases](./leases.md) — `cost.budget`, `model.use`, `fs.read` / `fs.write`.
- [Delegation](./delegation.md) — child-job patterns.
- [Errors](./errors.md) — `BUDGET_EXHAUSTED`, `CANCELLED`, `TIMEOUT`.
