# ReasoningStreams

A primary agent emits its reasoning as a `kind: thought` stream. A
mirror peer runtime subscribes, runs a smaller critic model on each
thought, and delegates `arcpx.mirror.critique.v1` events back into
the primary's session. Bounded by a token budget — when spent, the
mirror unsubscribes and the primary continues without critique.

## Before ARCP

Self-critique loops drift: there's no clean way to (a) expose just
the reasoning to a separate process for a second opinion, (b) cap
the rounds, (c) gracefully step back when the critic budget is
spent.

## With ARCP

```csharp
// mirror peer subscribes to the primary's thought stream...
await Request(mirror, Envelope(mirror, "subscribe", new Subscribe(new SubscribeFilter
{
    SessionId = [targetSessionId.Value],
    Types = ["stream.chunk"],
})));

// ...and delegates critiques back as namespaced events.
await Send(mirror, Envelope(mirror, "agent.delegate",
    new AgentDelegate(Target: targetSessionId.Value, Task: "consume_critique",
        Context: JsonSerializer.SerializeToElement(new { critique }))));
```

The mirror is a *peer runtime* (`agent_handoff: true`,
`subscriptions: true`, `trust_level: trusted`), not an Observer —
it both reads and writes back into the primary's session.

## ARCP primitives

- `kind: thought` reasoning streams — RFC §11.4.
- Subscriptions with type filter — §13.2.
- Custom extension event under `arcpx.<domain>.<name>.v<n>` — §21.1.
- `agent.delegate` for cross-runtime delivery — §14.
- `tokens.used` budget — §17.3.1.

## File tour

- `Program.cs` — boots primary + mirror, wires the critique queue.
- `Agents.cs` — primary + critic LLM stubs.
- `Stubs.cs` — elided client helpers.

## Variations

- Multiple mirrors (security / factuality / style) subscribed in
  parallel; primary merges critiques by severity.
- Persist critiques to the SQLite sink in [Subscriptions](../Subscriptions)
  for drift analysis.
- Replace the critic LLM with a deterministic verifier that returns
  `severity` from a rule set.
