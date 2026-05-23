# Vendor extensions

ARCP defines a reserved `x-vendor.*` namespace for custom envelope fields and
event kinds that are not part of the core spec (§5.1, §8.2, §15). Extensions
round-trip without loss through `Envelope.Extensions` and unknown event kinds
pass through to consumers unchanged.

## Envelope extensions (§5.1, §15)

Any top-level JSON key that is not a core ARCP field lands in
`Envelope.Extensions` (`Dictionary<string, JsonElement>`):

```json
{
  "arcp": "1.1",
  "id":   "msg_01J...",
  "type": "job.submit",
  "x-vendor.acme.priority": "high",
  "x-vendor.acme.region":   "us-east-1"
}
```

Read on the receive side:

```csharp
if (envelope.Extensions.TryGetValue("x-vendor.acme.priority", out var prio))
    Console.WriteLine(prio.GetString());
```

Write on the send side by populating `Extensions` before calling
`ITransport.SendAsync`:

```csharp
var env = new Envelope { /* ... */ };
env.Extensions["x-vendor.acme.priority"] = JsonSerializer.SerializeToElement("high");
await transport.SendAsync(env, ct);
```

## Vendor event kinds (§8.2)

Any event kind that starts with `x-vendor.` is permitted. Emit from an agent:

```csharp
await ctx.EmitEventAsync("x-vendor.acme.thumbnail",
    new { url = "https://cdn.example.com/thumb.png" }, ct);
```

Consume on the client:

```csharp
await foreach (var ev in handle.Events(ct))
{
    if (ev.Kind == "x-vendor.acme.thumbnail")
    {
        var url = ev.Body.GetProperty("url").GetString();
        // ...
    }
}
```

Clients that don't recognise an `x-vendor.*` kind MUST ignore it — never
treat an unknown kind as an error.

## OTel trace propagation

`Arcp.Otel` uses the vendor extension key
`x-vendor.opentelemetry.tracecontext` to carry W3C `traceparent` and
`tracestate` between peers. See [Observability](./observability.md) for
setup.

## Naming conventions

Follow these conventions to avoid collisions:

| Pattern                   | Use case                                 |
| ------------------------- | ---------------------------------------- |
| `x-vendor.<company>.<key>`| Stable company-specific extensions.      |
| `x-vendor.<product>.<key>`| Product-scoped extensions.               |

Do **not** register keys in the `x-vendor.arcp.*` sub-namespace — that is
reserved for future spec use.

## Round-trip guarantee

The SDK guarantees that any unknown top-level field on an incoming envelope is
preserved in `Envelope.Extensions` and re-serialised verbatim on the way out.
No information is lost when an intermediary forwards a message it doesn't
recognise.
