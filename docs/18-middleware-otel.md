---
title: OpenTelemetry middleware
sdk: csharp
spec_sections: ["§11"]
order: 18
kind: reference
---

`Arcp.Otel` wraps an `ITransport` with OpenTelemetry-flavored `ActivitySource` instrumentation. It depends only on `System.Diagnostics.DiagnosticSource` — no OTel SDK or exporter is pulled by the library.

## Wrap a transport

```csharp
using Arcp.Otel;

// Server side:
_ = server.AcceptAsync(serverTransport.WithTracing());

// Client side:
await using var client = await ArcpClient.ConnectAsync(clientTransport.WithTracing(), options);
```

## Span shape

For each envelope, the middleware:

1. Starts an `Activity` on `ActivitySource("Arcp.Transport")` named `arcp.send {type}` (producer) or `arcp.recv {type}` (consumer).
2. Sets the attribute keys listed in [`11-tracing.md`](./11-tracing.md).
3. Injects a W3C `traceparent` (and `tracestate` if present) on send into `envelope.extensions["x-vendor.opentelemetry.tracecontext"]`.
4. Extracts the same on receive and restores it as the parent `ActivityContext` before starting the consumer span.

## Consumer setup

```csharp
using var tp = Sdk.CreateTracerProviderBuilder()
    .AddSource(ArcpDiagnostics.TransportSourceName)
    .AddSource(ArcpDiagnostics.RuntimeSourceName)
    .AddOtlpExporter()
    .Build();
```

The names `Arcp.Transport` and `Arcp.Runtime` are deliberately namespaced to avoid colliding with consumer-defined `ActivitySource("Arcp")` instances.
