---
title: Architecture
sdk: csharp
spec_sections: ["§4", "§5"]
order: 2
kind: overview
---

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./diagrams/arcp-projects-dark.svg">
  <img alt="ARCP C# SDK project graph" src="./diagrams/arcp-projects-light.svg">
</picture>

## Project graph

- **`Arcp.Core`** is the wire-format reference. Every other library references it.
- **`Arcp.Client`** holds the `ArcpClient` and `JobHandle` types — the side that submits jobs.
- **`Arcp.Runtime`** holds `ArcpServer`, `JobManager`, `LeaseManager`, `SessionState` — the side that runs them.
- **`Arcp.AspNetCore`** mounts a runtime on Kestrel via `IEndpointRouteBuilder.MapArcp("/arcp")`.
- **`Arcp.Otel`** wraps an `ITransport` with `ActivitySource`-based instrumentation.
- **`Arcp.Hosting`** registers a runtime in an `IHostedService` for non-ASP.NET workers.
- **`Arcp`** is an umbrella meta-package that pulls Core + Client + Runtime in one `dotnet add package`.

## Wire format (spec §5)

Every message is a JSON object envelope:

```json
{
  "arcp": "1",
  "id": "msg_01J...",
  "type": "job.submit",
  "session_id": "sess_01J...",
  "trace_id": "4bf92f...",
  "job_id": null,
  "event_seq": null,
  "payload": { ... }
}
```

Unknown top-level fields are preserved verbatim in `Envelope.Extensions` (a `Dictionary<string, JsonElement>`) so vendor-extension hints round-trip without loss (spec §5.1).

## Transports (spec §4)

- `WebSocketTransport` — UTF-8 text frames carrying one JSON envelope each. Used by both client and server.
- `StdioTransport` — newline-delimited JSON over an arbitrary `Stream` pair. `StdioTransport.FromConsole()` covers the child-process case.
- `MemoryTransport.Pair()` — in-process tests and same-process runtime hosting.
