# Observability

ARCP uses W3C Trace Context (spec §11). The SDK exposes instrumentation via
`System.Diagnostics.ActivitySource` — no OpenTelemetry SDK dependency in the
core library. `Arcp.Otel` provides the transport wrapper and attribute
constants.

## Quick start

1. Add the `Arcp.Otel` package:

    ```sh
    dotnet add package Arcp.Otel
    ```

2. Wrap your transport with `WithTracing()`:

    ```csharp
    using Arcp.Otel;

    // Server side:
    _ = server.AcceptAsync(serverTransport.WithTracing());

    // Client side:
    await using var client = await ArcpClient.ConnectAsync(
        clientTransport.WithTracing(), options);
    ```

3. Register the `ActivitySource` names with your OTel SDK consumer:

    ```csharp
    using var tracerProvider = Sdk.CreateTracerProviderBuilder()
        .AddSource(ArcpDiagnostics.TransportSourceName)   // "Arcp.Transport"
        .AddSource(ArcpDiagnostics.RuntimeSourceName)     // "Arcp.Runtime"
        .AddOtlpExporter()
        .Build();
    ```

## ActivitySource names

| Name              | Purpose                                            |
| ----------------- | -------------------------------------------------- |
| `Arcp.Transport`  | One span per envelope (send and receive).          |
| `Arcp.Runtime`    | Runtime-internal spans (dispatch, agent run).      |

## Span shape

For each envelope, the wrapper:

1. Starts an `Activity` named `arcp.send {type}` (producer) or
   `arcp.recv {type}` (consumer).
2. Sets the attribute keys listed below.
3. On **send**: injects a W3C `traceparent` (and `tracestate` if present)
   into `envelope.extensions["x-vendor.opentelemetry.tracecontext"]`.
4. On **receive**: extracts the same field and restores it as the parent
   `ActivityContext` before starting the consumer span.

## Attribute keys (spec §11)

| Attribute                  | Source field                                  |
| -------------------------- | --------------------------------------------- |
| `arcp.direction`           | `"in"` / `"out"`                              |
| `arcp.type`                | envelope `type`                               |
| `arcp.id`                  | envelope `id`                                 |
| `arcp.session_id`          | envelope `session_id`                         |
| `arcp.job_id`              | envelope `job_id`                             |
| `arcp.trace_id`            | envelope `trace_id`                           |
| `arcp.event_seq`           | envelope `event_seq`                          |
| `arcp.agent`               | `payload.agent` (on submit / accept)          |
| `arcp.lease.capabilities`  | comma-joined lease keys                       |
| `arcp.lease.expires_at`    | ISO 8601 string (v1.1)                        |
| `arcp.budget.remaining`    | JSON-stringified currency map (v1.1)          |

## Use with ASP.NET Core

When hosting via `Arcp.AspNetCore`, call `WithTracing()` on the transport
returned by `MapArcp`:

```csharp
app.MapArcp(server, o =>
{
    o.Path            = "/arcp";
    o.TransportFilter = t => t.WithTracing();   // wrap each new connection
});
```

## Propagating trace IDs to child jobs

```csharp
// Pass the parent's trace ID when delegating:
var child = await childClient.SubmitAsync("research", traceId: ctx.TraceId);
```

This ensures the child's spans attach to the same distributed trace root.
See [Delegation](./delegation.md) for the full pattern.

## Runnable example

See [`samples/Tracing/`](../../samples/Tracing/) for a runnable sample
that exports spans to an OTLP collector.
