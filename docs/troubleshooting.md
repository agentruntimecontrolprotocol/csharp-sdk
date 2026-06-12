# Troubleshooting

## Connection failures

### `UnauthenticatedException` on connect

The token was rejected. Check:

1. `ArcpClientOptions.Token` matches a token configured in
   `StaticBearerVerifier` (or is accepted by your custom `IBearerVerifier`).
2. The token has not expired if you use time-bounded JWTs.
3. The `Authorization` header reaches the WebSocket upgrade — some reverse
   proxies strip bearer headers before forwarding.

### `session.error` received instead of `session.welcome`

The runtime sends `session.error` when authentication fails or when the
requested features are incompatible. The SDK surfaces this as
`UnauthenticatedException` or `InvalidRequestException`. Enable debug logging
on `Arcp.Runtime` to see the rejection reason.

### WebSocket upgrade returns `403 Forbidden`

`AllowedHosts` on `MapArcp` rejected the `Host` header. Either add the
incoming hostname to the list or remove `AllowedHosts` (not recommended in
production).

---

## Job failures

### `AgentVersionNotAvailableException`

The agent name or version is not registered on the server. Check:

1. `server.RegisterAgent("name", handler)` or
   `server.RegisterAgentVersion("name", "x.y.z", handler)` was called before
   `AcceptAsync`.
2. The version string in `SubmitAsync` matches exactly (case-sensitive).

### `DuplicateKeyException` (`DUPLICATE_KEY`)

Two submissions share an `idempotencyKey` but have different `agent` or
`input`. Either change the key or make the payloads identical.

### `TimeoutException` after `SubmitAsync`

`maxRuntimeSec` elapsed. Re-submit with a larger limit or break the work into
smaller jobs.

### `LeaseExpiredException` mid-job

`LeaseConstraints.ExpiresAt` fired before the agent finished. Increase the
`ExpiresAt` window or checkpoint work to resume after re-submit.

### `BudgetExhaustedException`

A `cost.budget` counter reached zero. Options:

- Increase the budget ceiling in the lease.
- Have the agent do non-cost-bearing work instead of failing.
- Submit a follow-up job with a fresh budget.

### `ResumeWindowExpiredException`

The client reconnected outside the `resume_window_sec` (default 600 s). The
SDK cannot replay missed events. Re-submit any jobs the client cares about.

---

## Heartbeat issues

### Job is cancelled with `HEARTBEAT_LOST`

The `session.pong` reply did not arrive within the timeout. Common causes:

- A network middlebox is silently dropping WebSocket frames.
- The client process was paused (GC stop-the-world, container throttle).
- The runtime and client clocks are skewed enough to affect the deadline.

Set `KeepAliveInterval = Timeout.InfiniteTimeSpan` on
`app.UseWebSockets(new WebSocketOptions { ... })` to prevent TCP-level and
ARCP-level pings from racing.

---

## Tracing issues

### Spans not appearing in the backend

1. `Arcp.Otel` package is installed.
2. `transport.WithTracing()` was called on both sides.
3. `ArcpDiagnostics.TransportSourceName` and
   `ArcpDiagnostics.RuntimeSourceName` are both registered with the
   `TracerProviderBuilder`.

### `traceparent` is not propagated to child jobs

`SubmitAsync` has no `traceId` parameter — trace context flows through
`Activity.Current`. Wrap the child submit in an activity started from
`ArcpDiagnostics.Runtime` (or any source that produces the parent's
trace ID). See
[Observability — propagating trace IDs](./guides/observability.md).

---

## Serialization issues

### `JsonException` on receive

An incoming envelope has a malformed payload. The most common cause is a
version mismatch between the client and server SDK. Verify both sides use
compatible `arcp` wire versions (the envelope `arcp` field, default `"1.1"`).

### Vendor extension fields disappear

Unknown top-level fields round-trip via `Envelope.Extensions`. If they vanish,
an intermediate forwarder may be re-serialising without round-tripping
extensions. Ensure any forwarder passes `Envelope.Extensions` through
unchanged.

---

## Diagnostics

Enable verbose SDK logs with the `Arcp.*` category:

```json
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Arcp": "Debug"
    }
  }
}
```

Or in code:

```csharp
builder.Logging.AddFilter("Arcp", LogLevel.Debug);
```

The SDK uses `[LoggerMessage]` source-generated logging throughout — log
entries carry structured properties (`session_id`, `job_id`, `event_seq`)
that are queryable in structured log sinks.
