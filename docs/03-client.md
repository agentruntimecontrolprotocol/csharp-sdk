---
title: Client
sdk: csharp
spec_sections: ["§6.1", "§7.1"]
order: 3
kind: reference
---

`ArcpClient` is the entry point for talking to an ARCP runtime. It owns one session, multiplexes job submissions, and exposes typed handles per job.

## Connect

```csharp
await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "my-app", Version = "1.0.0" },
    Token = bearerToken,             // optional bearer credential (§6.1)
    Features = FeatureSet.AllFeatures, // default: every v1.1 flag
});
```

After `ConnectAsync` completes, `client.SessionId`, `client.EffectiveFeatures`, `client.ResumeToken`, `client.Agents`, and `client.HeartbeatIntervalSec` carry the welcome payload.

## Submit a job

```csharp
JobHandle handle = await client.SubmitAsync(
    agent: "code-refactor@2.0.0",
    input: new { repo = "/workspace/app" },
    leaseRequest: new Lease(new Dictionary<string, IReadOnlyList<string>>
    {
        ["fs.read"]  = new[] { "/workspace/**" },
        ["fs.write"] = new[] { "/workspace/src/**" },
    }),
    leaseConstraints: new LeaseConstraints
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    },
    idempotencyKey: "refactor-2026-W19");
```

## Observe events

```csharp
await foreach (var ev in handle.Events(cancellationToken))
{
    Console.WriteLine($"{ev.Kind} seq={ev.EventSeq}");
}
var result = await handle.Result;
```

## Cancel

```csharp
await handle.CancelAsync(reason: "user-requested");
```

Cancellation is reserved for the submitting session — subscribers cannot cancel (spec §7.6).

## Other entry points

| Method | Spec | Purpose |
| ------ | ---- | ------- |
| `AckAsync(long lastProcessedSeq)` | §6.5 | Declare flow-control progress. |
| `ListJobsAsync(filter?, limit?, cursor?)` | §6.6 | Read-only job inventory. |
| `SubscribeAsync(jobId, history?)` | §7.6 | Attach to a job from another session. |
| `UnsubscribeAsync(jobId)` | §7.6 | Stop receiving fan-out events. |
