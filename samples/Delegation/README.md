# Delegation

Research orchestrator that fans a single request out to three peer
runtimes via `agent.delegate`, demultiplexes their event streams,
tolerates per-peer failure.

## Before ARCP

Each peer agent is reached over its own bespoke HTTP/SSE endpoint.
The orchestrator stands up three separate sockets, parses three
different event formats, writes three retry loops. Trace context is
"added later" and never quite makes it across the seam.

## With ARCP

```csharp
TraceId traceId = new($"trace_{Guid.NewGuid():N}"[..18]);
foreach (string peer in peers)
{
    DelegatedJob job = await DelegateAsync(client, peer, request, traceId);
    if (job.JobId is { } jid) mux.Register(jid);
    jobs.Add(job);
}
DelegatedJob[] completed = await Task.WhenAll(jobs.Select(j => Collect(mux, j)));
```

One transport, one envelope shape, one trace. Per-peer failure is a
typed `job.failed` envelope.

## ARCP primitives

- `agent.delegate` + `trace_id` propagation — RFC §14, §17.1.
- Job lifecycle (accepted → terminal) — §10.2.
- Stream/event multiplexing across `job_id` — §6.4.

## File tour

- `Program.cs` — fan-out, gather, synthesize. `JobMux` demuxes
  `Events()` by `job_id` so per-job consumers don't starve.
- `Synth.cs` — final-pass synthesizer (stubbed).
- `Stubs.cs` — elided client helpers.

## Variations

- Bound the fan-out by capability (e.g. only peers advertising
  `arcpx.research.web.v1`).
- Return artifact refs from peers (`job.completed.result_ref`)
  instead of inline results when payloads cross the inline budget (§16).
- Cancel slowest peer once N succeed via `cancel`
  (see [Cancellation](../Cancellation)).
