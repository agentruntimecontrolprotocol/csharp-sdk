---
title: Streamed results
sdk: csharp
spec_sections: ["§8.4"]
order: 16
kind: guide
---

For large final results (multi-MB reports, generated artifacts, model outputs), the agent emits a sequence of `result_chunk` events terminated by a normal `job.result` referencing the assembled chunks (spec §8.4).

## Server side

```csharp
server.RegisterAgent("report", async (ctx, ct) =>
{
    var rid = ctx.BeginResultStream();
    await ctx.WriteChunkAsync(rid, "Title\n", more: true, ct);
    await ctx.WriteChunkAsync(rid, "Section A: ...\n", more: true, ct);
    await ctx.WriteChunkAsync(rid, "End.\n", more: false, ct);
    return "report generated, 3 chunks";  // becomes summary
});
```

Binary chunks: pass a `ReadOnlyMemory<byte>` overload — the runtime base64-encodes for transport.

## Client side

```csharp
var handle = await client.SubmitAsync("report");
var assembled = new StringBuilder();
await foreach (var chunk in handle.Chunks(cancellationToken))
{
    assembled.Append(chunk.DecodedString);
}
var terminal = await handle.Result;
// terminal.Result.ResultId, .ResultSize, .Summary
```

## Invariants (spec §8.4)

- `chunk_seq` is 0-based monotonic per `result_id`.
- The final chunk has `more: false`.
- `encoding ∈ { "utf8", "base64" }`.
- A job MUST NOT mix inline `result` and `result_chunk`. `Job.BeginResultStream` enforces this.

See [`samples/ResultChunk/`](../samples/ResultChunk/).
