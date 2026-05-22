# Recipe: stream-resume

Demonstrates EventLog replay after a mid-stream disconnect.  Three independent
ARCP sessions collaborate:

```
Session 1 (submitter)  → submits "writer" job; keeps job alive for its duration
Session 2 (observer-1) → subscribes live; reads 3 of 8 chunks; disconnects
                          saves ResumeToken before closing
Session 3 (observer-2) → reconnects with ResumeToken + SubscribeAsync(history:true)
                          replays all 8 chunks from the EventLog
```

## What this demonstrates

| Feature | Spec ref |
| ------- | -------- |
| Submitter session keeps job alive independent of observers | §5.2 |
| `ctx.BeginResultStream` / `ctx.WriteChunkAsync` — streaming results | §12.3 |
| `client.SubscribeAsync(jobId, history: false)` — live subscription | §14 |
| `break` inside `await foreach (var chunk in handle.Chunks())` | §14 |
| `client.ResumeToken` — token persisted across disconnect | §5.3 |
| `ArcpClientOptions.ResumeToken` — reconnect with prior identity | §5.3 |
| `SubscribeAsync(history: true)` — EventLog replay from offset 0 | §14.2 |

## Key design

The submitter session is kept alive (`await using`) until the job completes.
This is the crucial difference from a single-session design: when observer-1
disconnects, the writer agent continues running because the _submitter_ session
still holds the job open.  Observer-2 can then subscribe after the job has
already finished and receive the full EventLog replay.

## Run

```sh
dotnet run --project recipes/stream-resume
```

Expected output includes:
- 3 lines from observer-1 (`obs1 ← seq 0`, `seq 1`, `seq 2`)
- "writer finished — success: True"
- "obs2 replayed 8 chunks"
- The full reassembled text from all 8 chunks

## Related

- [Resume guide](../../docs/guides/resume.md)
- [Job events guide](../../docs/guides/job-events.md)
- [`samples/Resume`](../../samples/Resume/) — session resume basics
- [`samples/ResultChunk`](../../samples/ResultChunk/) — `WriteChunkAsync` in isolation
- [`samples/Subscribe`](../../samples/Subscribe/) — `SubscribeAsync` in isolation
