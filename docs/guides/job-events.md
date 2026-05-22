# Job events

All in-progress signals from an agent travel as `job.event` envelopes,
discriminated on `payload.kind`. There is exactly one `job.event` envelope
type — the body shape depends on the kind (spec §8.1).

## Reserved kinds (§8.2)

| Kind           | Body                                              | Emitter |
| -------------- | ------------------------------------------------- | ------- |
| `log`          | `{ level, message }`                              | `ctx.LogAsync` |
| `thought`      | `{ text }`                                        | `ctx.ThoughtAsync` |
| `tool_call`    | `{ tool, call_id, args }`                         | `ctx.ToolCallAsync` |
| `tool_result`  | `{ call_id, result?, error? }`                    | `ctx.ToolResultAsync` |
| `status`       | `{ phase, message? }`                             | `ctx.StatusAsync` |
| `metric`       | `{ name, value, unit?, dimensions? }`             | `ctx.MetricAsync` |
| `artifact_ref` | `{ uri, content_type?, byte_size?, sha256? }`     | `ctx.ArtifactRefAsync` |
| `delegate`     | `{ child_job_id, agent, input? }`                 | `ctx.DelegateAsync` |
| `progress`     | `{ current, total?, units?, message? }`           | `ctx.ProgressAsync` (v1.1) |
| `result_chunk` | `{ result_id, chunk_seq, data, encoding, more }`  | `ctx.WriteChunkAsync` (v1.1) |

## Emitting events from an agent

```csharp
server.RegisterAgent("researcher", async (ctx, ct) =>
{
    await ctx.StatusAsync("starting", "Fetching data…", ct);

    await ctx.ToolCallAsync("fetch", callId: "c1",
        args: new { url = "https://api.example.com/data" }, ct);
    var data = /* … */ "";
    await ctx.ToolResultAsync("c1", result: data, ct);

    await ctx.ProgressAsync(current: 1, total: 3, message: "fetched", ct);

    await ctx.LogAsync("info", "Processing …", ct);
    await ctx.MetricAsync("cost.inference", 0.012m, unit: "USD", ct);

    await ctx.ArtifactRefAsync(
        uri: "s3://bucket/report.pdf",
        contentType: "application/pdf",
        byteSize: 42_000,
        ct: ct);

    return new { status = "done" };
});
```

## Consuming events on the client

```csharp
await foreach (var ev in handle.Events(cancellationToken))
{
    Console.WriteLine($"[{ev.EventSeq}] {ev.Kind}");
    if (ev.Kind == "log")
        Console.WriteLine($"  {ev.Body.GetProperty("message").GetString()}");
}
var result = await handle.Result;
```

## Vendor extensions

Any kind starting with `x-vendor.` is allowed. Emit one from an agent:

```csharp
await ctx.EmitEventAsync("x-vendor.acme.thumbnail",
    new { url = "https://cdn.example.com/thumb.png" }, ct);
```

Consume it on the client:

```csharp
if (ev.Kind == "x-vendor.acme.thumbnail")
{
    var url = ev.Body.GetProperty("url").GetString();
}
```

Unknown kinds are passed through without loss; clients that don't recognise
them should ignore them.

## Sequence numbers (§8.3)

`event_seq` is session-scoped, strictly monotonic, and gap-free across
reconnects within the resume window. The runtime's `EventLog` stamps every
event on emission.

`session.ping`, `session.pong`, and `session.ack` are control messages —
they do **not** consume `event_seq` values.

## Subscribe — cross-session observation (§7.6)

A subscriber observes a job submitted by a different session (or an earlier
session). Subscribers cannot cancel jobs.

```csharp
// In the observer session:
var sub = await observer.SubscribeAsync(jobId, history: true);
await foreach (var ev in sub.Events(cancellationToken))
{
    Console.WriteLine($"{ev.Kind} seq={ev.EventSeq}");
}
await sub.UnsubscribeAsync();
```

The runtime delivers `job.subscribed` on acknowledgement. With
`history: true`, buffered events are replayed under the **subscriber's**
session-scoped `event_seq` space.

## Resume vs subscribe

| Property             | Resume                    | Subscribe                   |
| -------------------- | ------------------------- | --------------------------- |
| Same session         | Yes                       | No                          |
| Replays history      | Mandatory                 | Optional (`history: true`)  |
| Cancel authority     | Yes                       | **No**                      |
| Requires resume_token| Yes                       | No                          |

Use **resume** when reconnecting after a network drop. Use **subscribe** for
dashboards, auditors, or multi-pane UIs. See [Resume](./resume.md) for the
reconnect pattern.
