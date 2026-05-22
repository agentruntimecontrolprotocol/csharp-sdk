# Sessions

A session is the unit of authentication and capability negotiation between a
client and a runtime. It begins with `session.hello`, is acknowledged by
`session.welcome`, and ends with `session.bye`.

## Hello / welcome (§6.2)

`ArcpClient.ConnectAsync` sends `session.hello` and waits for
`session.welcome` before returning:

```csharp
await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client   = new ClientInfo { Name = "my-app", Version = "1.0.0" },
    Token    = bearerToken,
    Features = FeatureSet.AllFeatures,   // default
});

// populated from session.welcome:
Console.WriteLine(client.SessionId);
Console.WriteLine(client.ResumeToken);
Console.WriteLine(client.HeartbeatIntervalSec);
foreach (var agent in client.Agents)
    Console.WriteLine($"{agent.Name} default={agent.Default}");
```

Wire shape of the two envelopes:

```json
// client → runtime
{
  "type": "session.hello",
  "payload": {
    "client": { "name": "my-app", "version": "1.0.0" },
    "auth":   { "scheme": "bearer", "token": "..." },
    "capabilities": {
      "encodings": ["json"],
      "features":  ["heartbeat", "ack", "subscribe", "result_chunk", "..."]
    }
  }
}

// runtime → client
{
  "type": "session.welcome",
  "session_id": "sess_01J...",
  "payload": {
    "runtime": { "name": "my-runtime", "version": "1.1.0" },
    "resume_token": "rt_...",
    "resume_window_sec": 600,
    "heartbeat_interval_sec": 30,
    "capabilities": {
      "encodings": ["json"],
      "features": ["heartbeat", "ack", "..."],
      "agents": [
        { "name": "code-refactor", "versions": ["1.0.0", "2.0.0"], "default": "2.0.0" }
      ]
    }
  }
}
```

## Feature negotiation

The effective feature set is `intersect(hello.features, welcome.features)`.
Both peers MUST NOT use features outside the negotiated set.

Opt out of features on the client:

```csharp
new ArcpClientOptions
{
    Features = new FeatureSet(["heartbeat", "ack"]),   // drop the rest
};
```

Opt out on the server (applies to all sessions):

```csharp
new ArcpServerOptions
{
    SupportedFeatures = new FeatureSet(["heartbeat", "ack"]),
};
```

## Heartbeat (§6.4)

When the `heartbeat` feature is negotiated, the runtime sends `session.ping`
every `heartbeat_interval_sec` seconds; the client MUST reply with
`session.pong`. Heartbeat messages are control messages — they do NOT consume
`event_seq` values.

The SDK handles ping/pong automatically. Configure the interval on the server:

```csharp
new ArcpServerOptions
{
    HeartbeatIntervalSec = 30,   // default
};
```

If the client misses two consecutive pongs, the runtime closes the session
with `HEARTBEAT_LOST`.

## Ack (§6.5)

`session.ack` tells the runtime the highest `event_seq` the client has
durably processed. The runtime uses this for back-pressure and to trim the
replay buffer.

```csharp
await client.AckAsync(lastProcessedSeq: 42);
```

The runtime emits a `status` back-pressure event when the unacknowledged
window exceeds `ArcpServerOptions.BackPressureThreshold`.

## List jobs (§6.6)

```csharp
var page = await client.ListJobsAsync(
    filter: new JobListFilter
    {
        Status       = new[] { "running", "pending" },
        Agent        = "code-refactor",
        CreatedAfter = DateTimeOffset.UtcNow.AddDays(-1),
    },
    limit: 25);

foreach (var job in page.Jobs)
    Console.WriteLine($"{job.JobId} agent={job.Agent} status={job.Status}");

if (page.NextCursor is { } cursor)
    var next = await client.ListJobsAsync(cursor: cursor);
```

By default the runtime returns only jobs the authenticated principal
submitted. A deployment-level `IJobAuthorizationPolicy` can widen this.

## Session close (§6.7)

`DisposeAsync` sends `session.bye` before closing the transport:

```csharp
await client.DisposeAsync();   // sends session.bye { reason: "normal" }
```

To send a custom reason:

```csharp
await client.CloseAsync(reason: "maintenance");
```

The runtime also sends `session.bye` before rejecting re-auth or on internal
shutdown. Listen for it via `client.SessionClosed` if you need to act on
runtime-initiated termination.

## Related guides

- [Authentication](./auth.md) — bearer tokens, `IBearerVerifier`.
- [Resume](./resume.md) — reconnect after a drop.
- [Heartbeat & ack (conformance)](../conformance.md) — feature negotiation table.
