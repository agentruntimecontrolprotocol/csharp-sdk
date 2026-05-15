---
title: Events
sdk: csharp
spec_sections: ["§8.1", "§8.2", "§8.3"]
order: 8
kind: reference
---

All in-progress signals from an agent travel as `job.event` envelopes, discriminated on `payload.kind`. There is exactly one `job.event` envelope type — the body shape depends on the kind (spec §8.1).

## Reserved kinds (§8.2)

| Kind           | Body                                         | Emitter |
| -------------- | -------------------------------------------- | ------- |
| `log`          | `{ level, message }`                         | `ctx.LogAsync` |
| `thought`      | `{ text }`                                   | `ctx.ThoughtAsync` |
| `tool_call`    | `{ tool, call_id, args }`                    | `ctx.ToolCallAsync` |
| `tool_result`  | `{ call_id, result?, error? }`               | `ctx.ToolResultAsync` |
| `status`       | `{ phase, message? }`                        | `ctx.StatusAsync` |
| `metric`       | `{ name, value, unit?, dimensions? }`        | `ctx.MetricAsync` (also feeds the budget ledger) |
| `artifact_ref` | `{ uri, content_type?, byte_size?, sha256? }`| `ctx.ArtifactRefAsync` |
| `delegate`     | `{ child_job_id, agent, input? }`            | `ctx.DelegateAsync` |
| `progress`     | `{ current, total?, units?, message? }`      | `ctx.ProgressAsync` (v1.1) |
| `result_chunk` | `{ result_id, chunk_seq, data, encoding, more }` | `ctx.WriteChunkAsync` (v1.1) |

## Vendor extensions

Any kind that starts with `x-vendor.` is allowed. Use `ctx.EmitEventAsync("x-vendor.acme.thumbnail", body)` to emit; clients can read it via `ev.Body.GetRawText()`.

## Sequence (§8.3)

`event_seq` is session-scoped, monotonic, gap-free across reconnects within the resume window. The runtime's `EventLog` stamps every event on emission. `session.ping`, `session.pong`, and `session.ack` are control messages — they do NOT consume `event_seq` values.
