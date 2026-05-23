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

`Arcp.AspNetCore` does not have a built-in transport hook. To
instrument the server side, wrap the `ArcpServer.AcceptAsync` call
yourself with a `WithTracing()`-wrapped transport, or call `WithTracing`
on the client transport before passing it to `ArcpClient.ConnectAsync`:

```csharp
var transport = new WebSocketTransport(socket, ownsSocket: true).WithTracing();
await using var client = await ArcpClient.ConnectAsync(transport, options);
```

## Propagating trace IDs to child jobs

The runtime reads the ambient `Activity.Current` when emitting
envelopes. Run a child submit inside an activity scope to inherit the
parent's trace context:

```csharp
using var activity = ArcpDiagnostics.Runtime.StartActivity("delegate.research");
var child = await childClient.SubmitAsync("research", input: new { topic });
```

`ctx.TraceId` is available as a read-only view of the parent's trace
identifier when you need to log it. See [Delegation](./delegation.md)
for the full pattern.

## Runnable example

See [`samples/Tracing/`](../../samples/Tracing/) for a runnable sample
that exports spans to an OTLP collector.
