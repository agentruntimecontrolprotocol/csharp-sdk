# Samples

Each subdirectory is a self-contained .NET 10 console program that demonstrates
one ARCP feature in isolation.  All samples use `MemoryTransport.Pair()` for
in-process testing — no network required.

| Directory | What it shows |
| --------- | ------------- |
| [`CostBudget`](./CostBudget/) | `ctx.MetricAsync` and `ctx.Budget` — charge and inspect a USD cost budget |
| [`Delegate`](./Delegate/) | `ctx.DelegateAsync` — record a delegation in the job event stream |
| [`IdempotentRetry`](./IdempotentRetry/) | `idempotencyKey` on `SubmitAsync` — safe re-submission after a network fault |
| [`LeaseViolation`](./LeaseViolation/) | `LeaseManager.AuthorizeOperation` — enforce a lease and return a `ToolError` |
| [`Resume`](./Resume/) | `client.ResumeToken` + `ArcpClientOptions.ResumeToken` — reconnect with a prior session identity |
| [`ResultChunk`](./ResultChunk/) | `ctx.BeginResultStream` / `ctx.WriteChunkAsync` — streaming result chunks |
| [`Stdio`](./Stdio/) | `MemoryTransport` wiring; notes on `StdioTransport.FromConsole()` for real stdio |
| [`Subscribe`](./Subscribe/) | `client.SubscribeAsync(jobId, history: true)` — attach to a running or completed job |
| [`SubmitAndStream`](./SubmitAndStream/) | `handle.Events()` — consume the full job event stream while a job runs |
| [`VendorExtensions`](./VendorExtensions/) | `ctx.EmitEventAsync("x-vendor.*")` and `ev.Kind.StartsWith("x-vendor.")` |

## Run any sample

```sh
dotnet run --project samples/<Name>
```

## TypeScript note

The TypeScript SDK includes additional samples built on Bun, Express, and
Fastify that demonstrate HTTP-transport integration patterns.  Those are not
ported here because the .NET ecosystem uses `Arcp.AspNetCore` / Kestrel for
HTTP hosting (see [`Arcp.AspNetCore`](../docs/projects/Arcp.AspNetCore.md)).

## More complex examples

See [`recipes/`](../recipes/) for composed multi-feature demos that combine
leases, delegation, streaming, and observability in realistic end-to-end
scenarios.
