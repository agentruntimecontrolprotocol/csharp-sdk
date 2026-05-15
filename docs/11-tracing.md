---
title: Tracing
sdk: csharp
spec_sections: ["§11"]
order: 11
kind: reference
---

ARCP uses W3C Trace Context. The SDK exposes its instrumentation through `System.Diagnostics.ActivitySource` — no OpenTelemetry SDK dependency in the core library.

## ActivitySource names

| Name              | Source class | Purpose |
| ----------------- | ------------ | ------- |
| `Arcp.Transport`  | `Arcp.Otel.ArcpTracing`     | One span per envelope (in/out). |
| `Arcp.Runtime`    | `Arcp.Runtime.ArcpServer`   | Runtime-internal spans. |

Register them in your OpenTelemetry consumer:

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(ArcpDiagnostics.TransportSourceName)
    .AddSource(ArcpDiagnostics.RuntimeSourceName)
    .AddOtlpExporter()
    .Build();
```

## Wrap a transport with tracing

```csharp
using Arcp.Otel;

var traced = transport.WithTracing();
// 'traced' is an ITransport that emits a span per send/recv and injects/extracts
// W3C traceparent via the envelope.extensions["x-vendor.opentelemetry.tracecontext"] entry.
```

## Attribute keys (spec §11, plus v1.1 additions)

| Key                        | Source field |
| -------------------------- | ------------ |
| `arcp.direction`           | `"in"` / `"out"` |
| `arcp.type`                | envelope `type` |
| `arcp.id`                  | envelope `id` |
| `arcp.session_id`          | envelope `session_id` |
| `arcp.job_id`              | envelope `job_id` |
| `arcp.trace_id`            | envelope `trace_id` |
| `arcp.event_seq`           | envelope `event_seq` |
| `arcp.agent`               | `payload.agent` (on submit/accept) |
| `arcp.lease.capabilities`  | comma-joined lease keys |
| `arcp.lease.expires_at`    | ISO 8601 string (v1.1) |
| `arcp.budget.remaining`    | JSON-stringified currency map (v1.1) |

See [`samples/Tracing/`](../samples/Tracing/) for a runnable example.
