# 05 — Middleware / Host Adapters

The TS reference ships six host packages under
`../typescript-sdk/packages/middleware/{node,express,fastify,hono,bun,otel}`.
Five of those exist because Node has no single canonical HTTP host —
`@arcp/express`, `@arcp/fastify`, `@arcp/hono` all delegate to
`@arcp/node`'s `attachArcpUpgrade(server, options)` and only differ in
how each framework's `http.Server` is obtained. .NET does have a
canonical host (ASP.NET Core on Kestrel), so the C# fan-out collapses
to **two required projects plus one defensible add**, all sitting on
top of `Arcp.Core`'s `WebSocketTransport` and `Arcp.Runtime`'s
`ArcpRuntime`.

The list below cites spec §, TS path, current C# path (where it
exists), and the BCL / ASP.NET Core surface in use.

## 1. Adapter projects

| Project           | Mirrors TS                                                   | Status                | Justification                                                                                                                                                  |
| ----------------- | ------------------------------------------------------------ | --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Arcp.AspNetCore` | `@arcp/node` + `@arcp/express` + `@arcp/fastify` + `@arcp/hono` | new                   | One adapter per host. Kestrel + `Microsoft.AspNetCore.WebSockets` is the one server WebSocket stack on .NET 9/10 (audit §1 §4.1). `IEndpointRouteBuilder.MapArcp("/arcp", ...)` is the public seam.                       |
| `Arcp.Otel`       | `@arcp/middleware-otel`                                      | new                   | Parity with TS. Library depends only on `System.Diagnostics.DiagnosticSource` (BCL); OTel SDK + exporters stay in the consumer (Phase 3 rule).                 |
| `Arcp.Hosting`    | none — defensible add                                        | new (small)           | Generic-Host bootstrapper: `IServiceCollection.AddArcpRuntime(...)` registers an `IHostedService` that owns an `ArcpRuntime` for non-ASP.NET workers (Windows Service, console). 30 LOC; without it the only on-ramp is `WebApplication`, which is wrong for an in-process worker. **Keep.** |
| gRPC pass-through | none                                                         | **dropped**           | gRPC is HTTP/2 with framing that does not match §4.1 (WebSocket text frames with JSON payloads). Wrapping bidi-streaming over ARCP envelopes invents a transport the spec does not name, multiplies the conformance surface, and earns its keep nowhere in the 18-example matrix. `Bun.serve` parity in TS exists because Bun's WS stack is not Node's; no analogous .NET split exists. **Don't ship.** |

### Hosts explicitly rejected

| Host                                                | Reject reason                                                                                                                                                                                                                       |
| --------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OWIN (`Microsoft.Owin.*`)                           | OWIN is .NET Framework / Katana. Not maintained on .NET 9/10. Its `WebSocketAccept` delegate (`Func<IDictionary<string, object>, Task>`) is the wrong shape for `HttpContext.WebSockets.AcceptWebSocketAsync()`. No path forward.   |
| Classic ASP.NET (`System.Web.HttpContext`)          | `System.Web.dll` does not run on .NET 9/10. `Microsoft.AspNetCore.WebSockets` on Kestrel is the only path. Including a `System.Web` adapter would force a .NET Framework target — out of scope (BOOTSTRAP §1, `net9.0`/`net10.0`).  |
| ServiceStack                                        | Competing framework with its own routing, DI, and message bus. Wrapping its `IAppHost` adds a parallel surface that re-exports what ASP.NET Core already exposes. No TS analogue (Express/Fastify/Hono are *thin*; ServiceStack is *thick*).                                  |
| Carter                                              | A minimal-API sugar over ASP.NET Core endpoint routing. Anyone using Carter already has `IEndpointRouteBuilder`; `MapArcp("/arcp")` works untouched. Net-zero adapter — don't ship one.                                             |
| Nancy                                               | Self-host stack archived 2020. Same reason as ServiceStack but worse — no .NET 9 support at all.                                                                                                                                    |

## 2. `Arcp.AspNetCore`

### 2.1 Public surface

```csharp
public static class ArcpEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapArcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/arcp",
        Action<ArcpOptions>? configure = null);
}

public sealed class ArcpOptions
{
    public string Path { get; set; } = "/arcp";
    public IReadOnlyList<string>? AllowedHosts { get; set; }   // default: localhost-only in Development
    public int MaxFrameBytes { get; set; } = 1 * 1024 * 1024;  // §14 chunk cap headroom
    public int HeartbeatIntervalSec { get; set; } = 20;        // §6.4; advertised in welcome
    public int BackPressureThreshold { get; set; } = 256;      // §6.5 lag in events
    public Func<JobAuthorizationContext, ValueTask<bool>>?
        JobAuthorizationPolicy { get; set; }                   // §6.6 / §7.6
    public FeatureSet Features { get; set; } = FeatureSet.All; // §6.2
}
```

`MapArcp` returns `IEndpointConventionBuilder` so `.RequireAuthorization()`,
`.WithName("Arcp")`, and `.RequireHost(...)` compose. Options bind via
`IOptions<ArcpOptions>` registered by `services.AddArcp(o => ...)`.

### 2.2 WebSocket upgrade attachment

Two pieces — both required:

1. `app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = Timeout.InfiniteTimeSpan })`.
   See risk R-1 below for the `KeepAliveInterval` rule.
2. The endpoint handler:
   ```
   if (!ctx.WebSockets.IsWebSocketRequest)            -> 400
   if (!AllowedHosts.Contains(ctx.Request.Host.Host)) -> 403   // before AcceptWebSocketAsync
   var ws = await ctx.WebSockets.AcceptWebSocketAsync(...).ConfigureAwait(false);
   var transport = new WebSocketTransport(ws, ownsSocket: true);
   await runtime.AcceptAsync(transport, ctx.RequestAborted).ConfigureAwait(false);
   ```

`WebSocketTransport` (`Arcp.Core`) is the same type the client uses
(audit §5); ownership flag matches what TS's `WebSocketTransport(ws)`
implicitly assumes (consumer doesn't `ws.close()` itself).

### 2.3 Host-header / DNS-rebind defense

Mirror TS `@arcp/node`'s `hostHeaderAllowed` (see
`../typescript-sdk/packages/middleware/node/src/index.ts:81-91`):
strip port, lowercase compare, reject before upgrade. Default in
`ASPNETCORE_ENVIRONMENT=Development` is
`["localhost", "127.0.0.1", "[::1]"]`. In any other environment the
default is **null** (= allow all) and the project README must say
"set this in production" — same posture as TS, which leaves
`allowedHosts` `undefined` by default. Spec §14 (security) requires
TLS / `wss://` but is silent on Host-header check; the rebind defense
follows RFC 6455 §10.2 and the TS reference.

Reject is `403 Forbidden` with `Connection: close`, written before
`AcceptWebSocketAsync` so we never burn the upgrade.

### 2.4 v1.1 capability headers on upgrade

The HTTP-level handshake on the **server** side is normal Kestrel:
`HttpContext.Request.Headers` exposes every request header verbatim,
so any `x-arcp-*` capability hint sent by a client during `Upgrade`
is readable inside the endpoint before `AcceptWebSocketAsync` fires.
This is the easy direction.

The asymmetric direction — and the audit's documented caveat — is
**client-side**: `ClientWebSocket.HttpResponseHeaders` is exposed
only when the connection is built from a `HttpMessageInvoker`
overload added in .NET 7+, otherwise `ClientWebSocket.ConnectAsync`
discards the upgrade response. The Phase 3 plan resolves that with a
manual `HttpClient` 101 handshake; `Arcp.AspNetCore` is **not**
affected because it lives on the server side.

In ARCP, capability negotiation lives in the `session.hello` /
`session.welcome` payloads (§6.2), not in HTTP headers. The
upgrade-header path is for deployment-layer hints (e.g.,
`Sec-WebSocket-Protocol: arcp.v1.1` if the deployment ever adds
sub-protocol selection), not for the protocol-level handshake.

### 2.5 v1.0 → v1.1 delta for this adapter

`HeartbeatIntervalSec` is new on `ArcpOptions` and is forwarded into
the welcome payload's `heartbeat_interval_sec` (§6.4); the adapter
itself doesn't run the heartbeat — `ArcpRuntime` does via
`PeriodicTimer` keyed on each `SessionState`. The adapter only
**advertises** the interval and **plumbs** the `BackPressureThreshold`
and `JobAuthorizationPolicy` (§6.6, §7.6) through to the runtime.

## 3. `Arcp.Hosting`

```csharp
services.AddArcpRuntime(o => { o.Features = FeatureSet.All; });
services.AddSingleton<IAgent, MyAgent>();
```

Registers `ArcpRuntime` as a singleton plus an `IHostedService` that
opens the configured transport (stdio for a child-process worker;
`MemoryTransport` for tests; no socket because there's no Kestrel
here). Out of scope: any HTTP listener — that's `Arcp.AspNetCore`'s
job. Without this project a worker process has to materialize a
`WebApplication` just to host a runtime, which is the wrong shape.

### v1.0 → v1.1 delta

None at the adapter layer — the new feature flags are options on the
runtime, not on the host wiring.

## 4. `Arcp.Otel`

### 4.1 Mechanism

Wraps `ITransport` (Phase 4 architecture, §6 `Public API sketch`),
mirroring TS `withTracing(inner, { tracer })`. The TS version takes
an OTel `Tracer`; the C# version takes nothing — it uses
`ActivitySource("Arcp")`. The OTel SDK + exporters are the consumer's
responsibility (Phase 3 rule).

```csharp
public static class ArcpTracing
{
    public static ITransport WithTracing(this ITransport inner);
}
```

Send path: start `ActivitySource.StartActivity($"arcp.send {type}", ActivityKind.Producer)`,
attach attributes (table below), inject W3C `traceparent` +
`tracestate` into `envelope.extensions["x-vendor.opentelemetry.tracecontext"]`
(constant matches TS `OTEL_EXTENSION_NAME` at
`../typescript-sdk/packages/middleware/otel/src/index.ts:48`).

Recv path: probe `envelope.extensions[OTEL_EXTENSION_NAME]`, restore
parent context with `ActivityContext.TryParse(traceparent, tracestate, out var ctx)`,
start `ActivityKind.Consumer` with that parent, run the handler under
`Activity.Current = activity`.

### 4.2 Attribute parity table

Side-by-side with `../typescript-sdk/packages/middleware/otel/src/index.ts:extractAttributes`
(lines 139–184). Every key must match byte-for-byte so a trace built
across a C# + TS hop renders correctly in any backend.

| TS attribute key                                    | C# `Activity.SetTag` key                              | Source                                                  |
| --------------------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------- |
| `arcp.direction` (`"in"` \| `"out"`)                | `arcp.direction`                                      | direction parameter                                     |
| `arcp.type`                                         | `arcp.type`                                           | envelope `type`                                         |
| `arcp.id`                                           | `arcp.id`                                             | envelope `id`                                           |
| `arcp.session_id`                                   | `arcp.session_id`                                     | envelope `session_id`                                   |
| `arcp.job_id`                                       | `arcp.job_id`                                         | envelope `job_id`                                       |
| `arcp.trace_id`                                     | `arcp.trace_id`                                       | envelope `trace_id` (§11)                               |
| `arcp.event_seq`                                    | `arcp.event_seq`                                      | envelope `event_seq`                                    |
| `arcp.agent`                                        | `arcp.agent`                                          | `payload.agent` on `job.submit` / `job.accepted` (§7.5) |
| `arcp.lease.capabilities` (comma-joined keys)       | `arcp.lease.capabilities`                             | `Object.keys(payload.lease ?? payload.lease_request)`   |
| `arcp.lease.expires_at` (ISO 8601 string)           | `arcp.lease.expires_at`                               | `payload.lease_constraints.expires_at` (§9.5, v1.1)     |
| `arcp.budget.remaining` (JSON-stringified map)      | `arcp.budget.remaining`                               | `payload.budget` (§9.6, v1.1)                           |

Two C#-specific details:

- `Activity.SetTag` accepts `object?`; `arcp.event_seq` is set as
  `long`, not stringified, to match TS's `number`-typed value.
  Backends that flatten attribute types coerce uniformly.
- `arcp.budget.remaining` serializes with
  `JsonSerializer.Serialize(budget, ArcpJsonContext.Default.BudgetMap)`
  using the source-generated context from Phase 3 — not
  reflection-based `JsonSerializer.Serialize(budget)`.

### 4.3 W3C propagation

Inject: `Activity.Current.Id` is the `traceparent` value when
`Activity.Current.IdFormat == ActivityIdFormat.W3C` (the default since
.NET 5). `Activity.Current.TraceStateString` is the `tracestate`.
Baggage rides on `Baggage.Current` and is propagated as a separate
carrier key `"baggage"` inside the extension object, matching the OTel
JS propagator output exactly.

Extract: parse the carrier object with
`ActivityContext.TryParse(traceparent, tracestate, out var parentCtx)`,
then `ActivitySource.StartActivity(name, kind, parentCtx, tags)`.

### 4.4 Library dependency boundary

The package's `.csproj` references **only**:

- `Microsoft.Extensions.DependencyInjection.Abstractions` (for the
  `WithTracing` extension on `ITransport` to play nicely with options),
- `Arcp.Core` (for `ITransport`, `Envelope`).

No `OpenTelemetry.*` package reference. The OTel SDK pulls
`System.Diagnostics.DiagnosticSource` `Activity` data on its own once
the consumer adds `AddSource("Arcp")` to their `TracerProviderBuilder`.

### 4.5 v1.0 → v1.1 delta

Three new attribute keys (`arcp.lease.expires_at`,
`arcp.budget.remaining`, and the existing `arcp.lease.capabilities`
now starts surfacing v1.1 lease constraints in addition to capabilities).
The W3C propagation extension key name does not change.

## 5. Risks

- **R-1 — `WebSocketOptions.KeepAliveInterval` collides with §6.4.**
  Kestrel's default `KeepAliveInterval` is 2 minutes; the BCL silently
  sends WebSocket ping frames at that cadence, but ARCP's
  `session.ping`/`session.pong` is an *application-layer* heartbeat
  with a different envelope and a sequence-number exclusion rule
  (§6.4: ping/pong NOT counted in `event_seq`). Two heartbeat layers
  will race and the application layer will be the loser when a
  network blip drops the transport-layer keepalive first. Set
  `KeepAliveInterval = Timeout.InfiniteTimeSpan` in
  `Arcp.AspNetCore`'s `app.UseWebSockets(...)` registration and
  document it.

- **R-2 — `HttpContext.Request.Host` includes the port; spec compare
  must strip it.** `app.MapArcp("/arcp")` with `AllowedHosts = ["localhost"]`
  rejects every request when the listener is on `:7777`. Mirror
  `../typescript-sdk/packages/middleware/node/src/index.ts:88-90`:
  `request.Host.Host` (no `:port`) on the .NET side maps to the
  TS `.split(":", 1)[0]`. Lowercase before compare;
  `HostString.Host` is case-preserved.

- **R-3 — `ctx.RequestAborted` cancels too early for graceful close.**
  `HttpContext.RequestAborted` fires when the WebSocket closes,
  which is exactly when the runtime wants to drain
  `session.bye { reason }` and any final `job.error { code: HEARTBEAT_LOST }`.
  Pass `runtime.AcceptAsync(transport, ct)` a linked
  `CancellationTokenSource` whose token is `ctx.RequestAborted` but
  whose cancellation is **deferred** by the runtime's own
  resume-window timer (Phase 4, `SessionState`). Don't pass
  `ctx.RequestAborted` raw or the resume window is gone.

- **R-4 — `ActivitySource("Arcp")` name collision.** If the host
  app also names an `ActivitySource` `"Arcp"`, traces interleave.
  Use `"Arcp.Transport"` for the transport-level source in
  `Arcp.Otel`, reserve `"Arcp.Runtime"` for runtime-internal spans.
  Both names go in the package XML doc so the consumer's
  `AddSource(...)` calls are explicit.

- **R-5 — `WebSocketTransport(socket, ownsSocket: true)` double-close
  on cancellation.** When `RequestAborted` fires *and* the runtime
  has already called `transport.CloseAsync(...)`, ASP.NET Core's
  WebSocket middleware will try to close the socket a second time
  in the request-pipeline finally block. `WebSocket.CloseAsync` on
  an already-closed socket throws `WebSocketException`. The transport
  must swallow `WebSocketException` with `WebSocketError.InvalidState`
  in `DisposeAsync`. Test with `Microsoft.AspNetCore.TestHost`'s
  WebSocket client.

## 6. Open questions

- Should `Arcp.AspNetCore` ship a Razor / minimal-API
  `/.well-known/arcp` discovery endpoint? Spec doesn't define one.
  TS doesn't ship one. **No, unless it's added to the spec.**
- Should the runtime emit ETW / `EventSource` events in addition to
  `ILogger` + `ActivitySource`? It's the .NET-idiomatic thing for a
  library, but every consumer this SDK targets uses OTel through
  `ActivitySource`. **No — keep the surface at one diagnostic API.**
