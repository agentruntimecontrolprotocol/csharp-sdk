---
title: Job subscriptions and listing
sdk: csharp
spec_sections: ["§6.6", "§7.6"]
order: 14
kind: guide
---

A subscriber observes a job that was submitted in a different session (or earlier in the same session). Subscription is distinct from resume — it doesn't recover authority, and a subscriber cannot cancel.

## List jobs (§6.6)

```csharp
var page = await client.ListJobsAsync(filter: new JobListFilter
{
    Status = new[] { "running", "pending" },
    Agent  = "code-refactor",
    CreatedAfter = DateTimeOffset.UtcNow.AddDays(-1),
}, limit: 25);

foreach (var job in page.Jobs)
{
    Console.WriteLine($"{job.JobId} agent={job.Agent} status={job.Status}");
}

if (page.NextCursor is { } cursor)
{
    var next = await client.ListJobsAsync(cursor: cursor);
}
```

By default the runtime returns jobs the session's authenticated principal submitted. Deployment-level `IJobAuthorizationPolicy` widens that.

## Subscribe (§7.6)

```csharp
var sub = await observer.SubscribeAsync(jobId, history: true);
await foreach (var ev in sub.Events(cancellationToken))
{
    Console.WriteLine($"{ev.Kind} seq={ev.EventSeq}");
}
await sub.UnsubscribeAsync();
```

The runtime delivers a `job.subscribed` envelope on acknowledgement; if `history: true` the buffered events with `event_seq > from_event_seq` are replayed under the **subscriber's** session-scoped `event_seq` space.

## Resume vs subscribe

| Property            | Resume        | Subscribe          |
| ------------------- | ------------- | ------------------ |
| Same session        | Yes           | No (new session)   |
| Replays history     | Mandatory     | Optional (`history: true`) |
| Cancel authority    | Yes           | **No**             |
| Requires resume_token | Yes         | No                 |

Use resume for reconnecting after a network drop. Use subscribe for dashboards, auditors, or multi-pane UIs.
