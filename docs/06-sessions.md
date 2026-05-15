---
title: Sessions
sdk: csharp
spec_sections: ["§6.1", "§6.2", "§6.3"]
order: 6
kind: reference
---

A session is the unit of authentication and capability negotiation between a client and a runtime. It begins with `session.hello`, is acknowledged by `session.welcome`, and ends with `session.bye`.

## Hello / welcome (§6.2)

Client sends:

```json
{
  "type": "session.hello",
  "payload": {
    "client": { "name": "my-app", "version": "1.0.0" },
    "auth":   { "scheme": "bearer", "token": "..." },
    "capabilities": {
      "encodings": ["json"],
      "features":  ["heartbeat", "ack", "subscribe", "result_chunk", ...]
    }
  }
}
```

Runtime responds:

```json
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
      "features": ["heartbeat", "ack", ...],
      "agents":   [ { "name": "code-refactor", "versions": ["1.0.0", "2.0.0"], "default": "2.0.0" } ]
    }
  }
}
```

The effective feature set is `intersect(hello.features, welcome.features)` — both peers MUST NOT use features outside it.

## Resume (§6.3)

`resume_token` rotates on every welcome. To resume after a transport drop:

```csharp
await using var client = await ArcpClient.ConnectAsync(newTransport, new ArcpClientOptions
{
    Client = ...,
    Token = bearerToken,
    // resume token passing happens on session.hello.payload.resume_token
});
```

The runtime replays buffered events with `event_seq > last_event_seq` before resuming live streaming.

## Close (§6.7)

```csharp
await client.DisposeAsync(); // sends session.bye
```
