# Recipes

Composed ARCP features wired around a real LLM workload. Unlike the
single-feature [`samples/`](../samples/) — which use toy agents (echo,
timer, fake build) — each recipe is a complete end-to-end shape
demonstrating a protocol pattern with realistic agent behaviour.

Each recipe is a self-contained .NET console project. To run any recipe,
build it from its directory:

```bash
dotnet run --project recipes/<recipe-name>
```

---

## [multi-agent-budget/](multi-agent-budget/) — budget cascade

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../resources/diagrams/multi-agent-budget-dark.svg">
  <img alt="multi-agent-budget architecture" src="../resources/diagrams/multi-agent-budget-light.svg">
</picture>

The planner decomposes a research question into sub-questions and delegates
each to a worker carrying a budget slice carved from the planner's own
remaining cap. After each grant the planner emits a `cost.delegate` metric
on itself so the runtime's subset check at the next delegate sees an honest
remaining balance. Workers that overspend trip `BUDGET_EXHAUSTED`; sub-
questions that no longer fit are skipped before the delegate.

**Spec highlights:** §13.2 delegation + lease-subset enforcement, §9.6
`cost.budget` auto-decrement on `cost.*` metrics, and the
"debit-self-for-each-grant" pattern that turns ARCP's independent per-job
counters into a shared cascade.

---

## [email-vendor-leases/](email-vendor-leases/) — lease-scoped vendor credential delegation

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../resources/diagrams/email-vendor-leases-dark.svg">
  <img alt="email-vendor-leases architecture" src="../resources/diagrams/email-vendor-leases-light.svg">
</picture>

A triage agent runs a tool-use loop with three tools, but the lease grants
only the two read-only ones. When the model proposes `send_reply` the
agent's `LeaseManager.AuthorizeOperation` throws and feeds
`PERMISSION_DENIED` back as a tool-result error so the model can recover
gracefully — lease violations are not session-fatal. Each `inbox_read` also
emits an `x-vendor.acme.email.parsed` event so dashboards recognising the
namespace can render parsed metadata specially.

**Spec highlights:** §13.4 lease violation as a recoverable `tool_result`
error, §15 / §8.2 `x-vendor.*` event-kind namespace.

---

## [stream-resume/](stream-resume/) — `result_chunk` + resume after disconnect

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../resources/diagrams/stream-resume-dark.svg">
  <img alt="stream-resume architecture" src="../resources/diagrams/stream-resume-light.svg">
</picture>

A long-form writer agent pipes content deltas into `ctx.WriteChunkAsync`,
batching ~200 chars per `result_chunk` envelope. Every envelope lands in
the runtime's `EventLog` under a monotonic `event_seq`. The client drops
the transport mid-stream, opens a fresh one with `client.ResumeToken`, and
the runtime replays every envelope past the cutoff so reassembly completes
seamlessly across the gap.

**Spec highlights:** §8.4 `ctx.BeginResultStream()` / `WriteChunkAsync` /
chunked result assembly; §13.3 / §6.3 `EventLog` + `ResumeWindowSec`.

---

## [mcp-skill/](mcp-skill/) — MCP bridge

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../resources/diagrams/mcp-skill-dark.svg">
  <img alt="mcp-skill architecture" src="../resources/diagrams/mcp-skill-light.svg">
</picture>

An MCP server fronts the [multi-agent-budget](multi-agent-budget/)
planner so any MCP host (Claude Code, Cursor, Desktop) can call it as a
single `research` tool. The bridge keeps one long-lived ARCP session; each
MCP tool invocation submits a fresh planner job and returns the terminal
result as the tool's text response. A Claude Code skill at
[skills/research/SKILL.md](mcp-skill/skills/research/SKILL.md) tells the
model when to reach for the tool.

**Spec highlights:** the seam between MCP (model-side tool surface) and
ARCP (runtime-side agent execution); one ARCP session per bridge process.

---

Diagram sources live in [`resources/diagrams/`](../resources/diagrams/)
alongside the kit used to keep the light / dark variants in sync.
