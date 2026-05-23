# Session resume

After a transport drop, a client can reconnect and receive missed events
without re-submitting jobs (spec §6.3).

## How it works on the wire

1. `session.welcome` includes a `resume_token` and `resume_window_sec`.
2. The runtime maintains an in-memory ring buffer of the last
   `resume_window_sec` seconds of events for the session.
3. On reconnect, the client sends `session.hello` with the previous
   `resume_token` and `last_event_seq`. The runtime validates the token
   and replays buffered events where `event_seq > last_event_seq`.
4. The `resume_token` rotates on every `session.welcome` — cache the latest
   one, not the original.

## What the SDK exposes today

The runtime fully supports the server side of resume. The client captures
the latest `resume_token` so applications can keep it warm:

```csharp
await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "my-app", Version = "1.0.0" },
    Token  = bearerToken,
});

string? resumeToken = client.ResumeToken;
long    lastSeq     = client.LastReceivedSeq;
```

`ArcpClient.ResumeToken` and `ArcpClient.LastReceivedSeq` reflect the
most recent welcome and the highest event sequence the client has
observed.

> **Client-side resume is not yet wired through `ArcpClientOptions`.**
> The fields above let you save state, but reconnecting with them on a
> fresh `ArcpClient` requires direct envelope construction. Until the
> public `ConnectAsync` surface accepts a resume token, prefer
> `client.SubscribeAsync(jobId, history: true, ct)` from a new session
> to replay a specific job's events from the runtime's `EventLog`.

## Gap-free guarantee

The runtime uses a window-bounded replay buffer. If the client reconnects
within `resume_window_sec` and the requested `last_event_seq` is still in
the buffer, replay is gap-free. Outside the window the runtime returns
`RESUME_WINDOW_EXPIRED` — the client must re-submit any jobs it cares about.

## Resume token security

- The token is a cryptographically random opaque string.
- It is **session-specific** — it cannot be used to access another session's
  events.
- It rotates on every `session.welcome` to limit replay-attack windows.
- Store it in memory only; do not persist across application restarts unless
  you also persist the associated session state.

## Resume vs subscribe

| Property              | Resume                          | Subscribe                    |
| --------------------- | ------------------------------- | ---------------------------- |
| Same session          | Yes                             | No (new session)             |
| Requires resume token | Yes                             | No                           |
| Replays history       | Mandatory (from `last_event_seq`) | Optional (`history: true`) |
| Cancel authority      | Yes (preserved from original)   | **No**                       |

Use **resume** when reconnecting after a network drop in the same client
process. Use **subscribe** for dashboards, audit tools, or any second
session that wants to observe an existing job.
See [Job events — subscribe](./job-events.md#subscribe) for details.
