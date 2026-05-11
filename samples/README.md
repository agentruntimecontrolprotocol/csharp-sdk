# ARCP C# Samples

Fourteen single-purpose samples, each named for the protocol primitive it
demonstrates. Mirrors the canonical Python tree at
`../python-sdk/examples/`.

> **Illustrative, not runnable.** Each sample imports from `ARCP` as if
> the SDK already exposed every helper used. Setup boilerplate (transport
> URL, identity, auth) is elided with `ARCPClient client = null!`. LLM
> and framework calls live in tiny stub files (`Agents.cs`, `Steps.cs`,
> `Synth.cs`, ...) so the protocol code in `Program.cs` is what you read.

## The fourteen

| Directory | Demonstrates | Spec |
|---|---|---|
| [`Subscriptions/`](./Subscriptions) | Three Observer clients on one session, three filters, three sinks. | §5, §13 |
| [`Leases/`](./Leases) | Lease-gated shell agent. Read leases coarse, write leases scoped. | §15.4–§15.5 |
| [`LeaseRevocation/`](./LeaseRevocation) | Per-table leases with `lease.revoked` / `lease.extended` mid-flight. | §15.5 |
| [`PermissionChallenge/`](./PermissionChallenge) | Two-party permission challenge — generator asks, reviewer holds veto. | §15.4, §6.4 |
| [`Delegation/`](./Delegation) | `agent.delegate` fan-out + `JobMux` to demux events by `job_id`. | §14, §6.4 |
| [`Handoff/`](./Handoff) | `agent.handoff` with transcript packed as artifact, runtime fingerprint pinned. | §14, §16, §8.3 |
| [`Heartbeats/`](./Heartbeats) | Worker federation; heartbeat-loss reroute via `idempotency_key`. | §10.3, §6.4 |
| [`CapabilityNegotiation/`](./CapabilityNegotiation) | Capability-driven peer routing; standard `cost.usd` rollups. | §7, §17.3.1, §18.3 |
| [`Resumability/`](./Resumability) | **Actually crash and resume.** `Environment.Exit(137)` mid-flight; second invocation picks up at the next step. | §10, §19, §6.4 |
| [`ReasoningStreams/`](./ReasoningStreams) | `kind: thought` stream + a peer runtime that subscribes and delegates critiques back. | §11.4, §13, §14 |
| [`Extensions/`](./Extensions) | Custom `arcpx.sdr.*.v1` extension namespace with correct unknown-message handling. | §21 |
| [`HumanInput/`](./HumanInput) | `human.input.request` fanned across phone/email/Slack; first-wins resolution. | §12 |
| [`Cancellation/`](./Cancellation) | Cooperative `cancel` (terminate) vs `interrupt` (pause and ask). | §10.4–§10.5 |
| [`MCP/`](./MCP) | ARCP runtime fronting an MCP server: `tool.invoke` → MCP `call_tool`. | §20 |

## Conventions

- .NET 10, C# `latest`, top-level statements in `Program.cs`.
- Each sample is one `Program.cs` (the protocol code) + 0–2 stub
  files named for what they elide (`Agents.cs`, `Steps.cs`, `Cheap.cs`,
  `Synth.cs`, `Work.cs`, `Channels.cs`, `Sql.cs`, `Roster.cs`, ...).
- A per-project `Stubs.cs` provides the elided client helpers
  (`Open`, `Send`, `Request`, `Events`, `Envelope`) so the protocol
  code reads as straight-through SDK calls. They throw
  `NotImplementedException`.
- `ARCPClient client = null!; // transport, identity, auth elided` —
  setup boilerplate is not the point.
- Envelopes match RFC-0001 v2 exactly. Custom message types follow
  §21.1 `arcpx.<domain>.<name>.v<n>` naming.

## Reading order

For a brisk tour: `Subscriptions`, `Leases`, `Delegation`,
`Resumability` (this one actually crashes and recovers),
`Cancellation`, `Extensions`, `MCP`. These seven exercise the bulk
of the protocol.

## Numbered samples

`01.MinimalSession` ... `06.RelayHumanInTheLoop` are pre-existing
phase-1 placeholders unrelated to this set; they remain in the
solution and will be rewritten in a later phase.
