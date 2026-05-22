# Session resume

After a transport drop, a client can reconnect and receive missed events
without re-submitting jobs (spec §6.3).

## How it works

1. `session.welcome` includes a `resume_token` and `resume_window_sec`.
2. The runtime maintains an in-memory ring buffer of the last
   `resume_window_sec` seconds of events for the session.
3. On reconnect, the client sends `session.hello` with the previous
   `resume_token`. The runtime validates it and replays buffered events where
   `event_seq > last_event_seq`.
4. The `resume_token` rotates on every `session.welcome` — cache the latest
   one, not the original.

## Resume in C#

```csharp
string? resumeToken = null;
long    lastEventSeq = 0;

async Task ConnectWithResumeAsync(ITransport transport)
{
    await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
    {
        Client      = new ClientInfo { Name = "my-app", Version = "1.0.0" },
        Token       = bearerToken,
        ResumeToken = resumeToken,       // null on first connect
        LastEventSeq = lastEventSeq,     // 0 on first connect
    });

    // update resume state from the welcome payload:
    resumeToken = client.ResumeToken;

    await foreach (var ev in handle.Events(ct))
    {
        lastEventSeq = ev.EventSeq;
        // process ev …
    }
}
```

## Gap-free guarantee

The runtime uses a window-bounded replay buffer. If the client reconnects
within `resume_window_sec` and the requested `last_event_seq` is still in the
buffer, replay is gap-free. Outside the window the runtime returns
`RESUME_WINDOW_EXPIRED` — the client must re-submit any jobs it cares about.

## Resume token security

- The token is a cryptographically random opaque string.
- It is **session-specific** — it cannot be used to access another session's
  events.
- It rotates on every `session.welcome` to limit replay-attack windows.
- Store it in memory only; do not persist across application restarts unless
  you also persist the associated session state.

## Resume vs subscribe

| Property             | Resume                        | Subscribe                    |
| -------------------- | ----------------------------- | ---------------------------- |
| Same session         | Yes                           | No (new session)             |
| Requires resume token| Yes                           | No                           |
| Replays history      | Mandatory (from `last_seq`)   | Optional (`history: true`)   |
| Cancel authority     | Yes (preserved from original) | **No**                       |

Use **resume** when reconnecting after a network drop in the same client
process. Use **subscribe** for dashboards or audit tools in separate sessions.
See [Job events — subscribe](./job-events.md#subscribe) for details.
