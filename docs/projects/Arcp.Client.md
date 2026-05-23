# Arcp.Client

`Arcp.Client` provides `ArcpClient` — the entry point for talking to an ARCP
runtime. It owns one session, multiplexes job submissions, and exposes typed
handles per job.

```sh
dotnet add package Arcp.Client
```

> **Tip:** most apps reference the `Arcp` meta-package instead, which
> re-exports `Arcp.Client` along with `Arcp.Core` and `Arcp.Runtime`.

## Connect

```csharp
await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client   = new ClientInfo { Name = "my-app", Version = "1.0.0" },
    Token    = bearerToken,             // optional bearer credential (§6.1)
    Features = FeatureSet.AllFeatures,  // default: every v1.1 flag
});
```

After `ConnectAsync` completes the welcome payload is available:

| Property                    | Description                                 |
| --------------------------- | ------------------------------------------- |
| `client.SessionId`          | Server-assigned session identifier.         |
| `client.EffectiveFeatures`  | Negotiated feature intersection.            |
| `client.ResumeToken`        | Opaque token for reconnect replay.          |
| `client.Agents`             | Advertised agent names and versions.        |
| `client.HeartbeatIntervalSec` | Ping interval to maintain (spec §6.4).   |

## ArcpClientOptions

| Property       | Default                  | Purpose                                                       |
| -------------- | ------------------------ | ------------------------------------------------------------- |
| `Client`       | required                 | Name / version sent in `session.hello`.                       |
| `Token`        | `null`                   | Bearer token for authentication.                              |
| `AuthScheme`   | `"bearer"`               | `auth.scheme` sent on `session.hello`.                        |
| `Features`     | `FeatureSet.AllFeatures` | Feature flags to request.                                     |
| `Encodings`    | `["json"]`               | Envelope encodings to advertise.                              |
| `TimeProvider` | `TimeProvider.System`    | Clock injection for tests.                                    |

## Submit a job

```csharp
JobHandle handle = await client.SubmitAsync(
    agent:            "code-refactor@2.0.0",
    input:            new { repo = "/workspace/app" },
    leaseRequest:     new Lease(new Dictionary<string, IReadOnlyList<string>>
    {
        ["fs.read"]  = new[] { "/workspace/**" },
        ["fs.write"] = new[] { "/workspace/src/**" },
    }),
    leaseConstraints: new LeaseConstraints
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    },
    idempotencyKey:   "refactor-2026-W19",
    maxRuntimeSec:    3600);
```

## Observe events

```csharp
await foreach (var ev in handle.Events(cancellationToken))
{
    switch (ev.Kind)
    {
        case "log":     Console.WriteLine(ev.Body.GetProperty("message").GetString()); break;
        case "progress":
            int pct = ev.Body.GetProperty("pct").GetInt32();
            Console.WriteLine($"Progress: {pct}%");
            break;
    }
}
var result = await handle.Result;
```

## Cancel a job

```csharp
await handle.CancelAsync(reason: "user-requested");
```

Cancellation is reserved for the submitting session — subscribers cannot
cancel (spec §7.6).

## All entry points

| Method                                        | Spec   | Purpose                                           |
| --------------------------------------------- | ------ | ------------------------------------------------- |
| `ConnectAsync(transport, options, ct)`                       | §6.1   | Open session, receive welcome.                  |
| `SubmitAsync(agent, input, lease?, constraints?, idempotencyKey?, maxRuntimeSec?, parentJobId?, ct)` | §7.1 | Submit a job; returns `JobHandle`. |
| `CancelJobAsync(jobId, reason?, ct)`                         | §7.4   | Cancel by `JobId`.                              |
| `AckAsync(lastProcessedSeq, ct)`                             | §6.5   | Declare flow-control progress.                  |
| `ListJobsAsync(filter?, limit?, cursor?, ct)`                | §6.6   | Read-only job inventory with cursor pagination. |
| `SubscribeAsync(jobId, history?, ct)`                        | §7.6   | Attach to a job from another session.           |
| `UnsubscribeAsync(jobId, ct)`                                | §7.6   | Stop receiving fan-out events.                  |
| `DisposeAsync()`                                             | §6.7   | Send `session.bye`, close transport.            |

## JobHandle

`SubmitAsync` returns a `JobHandle` per job:

| Member                            | Description                                    |
| --------------------------------- | ---------------------------------------------- |
| `handle.JobId`                    | Assigned job identifier.                       |
| `handle.Events(ct)`               | `IAsyncEnumerable<JobEvent>` — ordered stream. |
| `handle.Result`                   | `Task<JobResult>` — awaits terminal state.     |
| `handle.Chunks`                   | `IAsyncEnumerable<ResultChunk>` for streaming results. |
| `handle.CancelAsync(reason?, ct)` | Request cancellation.                          |

## Related

- [Jobs guide](../guides/jobs.md) — full submit / lifecycle / streaming walkthrough.
- [Sessions guide](../guides/sessions.md) — hello/welcome, heartbeat, ack.
- [Resume guide](../guides/resume.md) — reconnect with `ResumeToken`.
- [Job events guide](../guides/job-events.md) — all 10 reserved event kinds.
