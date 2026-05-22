# Arcp.Otel

`Arcp.Otel` wraps any `ITransport` with OpenTelemetry-flavored
`System.Diagnostics.ActivitySource` instrumentation. The library depends
only on `System.Diagnostics.DiagnosticSource` — no OTel SDK or exporter is
pulled transitively.

```sh
dotnet add package Arcp.Otel
```

## Wrap a transport

Call `.WithTracing()` on both sides before handing the transport to the SDK:

```csharp
using Arcp.Otel;

// Server side:
_ = server.AcceptAsync(serverTransport.WithTracing(), ct);

// Client side:
await using var client = await ArcpClient.ConnectAsync(
    clientTransport.WithTracing(), options, ct);

// ASP.NET Core — wrap per connection via TransportFilter:
app.MapArcp(server, o => { o.TransportFilter = t => t.WithTracing(); });
```

## Register with the OTel SDK

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(ArcpDiagnostics.TransportSourceName)  // "Arcp.Transport"
    .AddSource(ArcpDiagnostics.RuntimeSourceName)    // "Arcp.Runtime"
    .AddOtlpExporter()
    .Build();
```

## ActivitySource names

| Name             | Covers                                              |
| ---------------- | --------------------------------------------------- |
| `Arcp.Transport` | One span per envelope (send and receive).           |
| `Arcp.Runtime`   | Runtime-internal spans (dispatch, agent run).       |

Both names are exposed as `const string` on `ArcpDiagnostics` so your
`TracerProviderBuilder` references are refactor-safe.

## Span shape

For each envelope the wrapper:

1. Starts an `Activity` named `arcp.send {type}` (producer) or
   `arcp.recv {type}` (consumer).
2. Sets attribute keys from the table below.
3. **Send**: injects a W3C `traceparent` (and `tracestate` if present) into
   `envelope.extensions["x-vendor.opentelemetry.tracecontext"]`.
4. **Receive**: extracts the same key and restores it as the parent
   `ActivityContext` before starting the consumer span.

## Attribute keys (spec §11)

| Attribute                    | Source                                   |
| ---------------------------- | ---------------------------------------- |
| `arcp.direction`             | `"in"` / `"out"`                         |
| `arcp.type`                  | envelope `type`                          |
| `arcp.id`                    | envelope `id`                            |
| `arcp.session_id`            | envelope `session_id`                    |
| `arcp.job_id`                | envelope `job_id`                        |
| `arcp.trace_id`              | envelope `trace_id`                      |
| `arcp.event_seq`             | envelope `event_seq`                     |
| `arcp.agent`                 | `payload.agent` (on submit / accept)     |
| `arcp.lease.capabilities`    | comma-joined lease keys                  |
| `arcp.lease.expires_at`      | ISO 8601 string (v1.1)                   |
| `arcp.budget.remaining`      | JSON-stringified currency map (v1.1)     |

## Propagating trace IDs to child jobs

```csharp
// Inside an orchestrator agent, pass the parent's trace ID when delegating:
var child = await childClient.SubmitAsync(
    "research",
    traceId: ctx.TraceId,  // <-- propagates the distributed trace root
    leaseRequest: childLease);
```

See [Delegation guide](../guides/delegation.md) for the full orchestrator
pattern and [Observability guide](../guides/observability.md) for the
complete OTel walkthrough.

## Related

- [Observability guide](../guides/observability.md) — full setup and attribute reference.
- [Arcp.AspNetCore](./Arcp.AspNetCore.md) — per-connection `TransportFilter` wiring.
- [Vendor extensions](../guides/vendor-extensions.md) — `x-vendor.opentelemetry.tracecontext` key.
- [Troubleshooting — spans not appearing](../troubleshooting.md#spans-not-appearing-in-the-backend).
