# Recipes

Recipes are self-contained runnable projects that demonstrate a complete
pattern. Each recipe lives in its own folder under `recipes/` and can be run
with `dotnet run`:

```sh
cd recipes/multi-agent-budget
dotnet run
```

## Available recipes

### multi-agent-budget

**Budget cascade in a multi-agent chain.**

An orchestrator agent delegates sub-tasks to two specialist agents. Each
delegation uses a child lease with a per-currency budget ceiling. The
orchestrator tracks remaining budget and stops delegating before exhaustion.

Concepts: `cost.budget`, `LeaseManager.AssertSubset`, `ctx.DelegateAsync`,
`ctx.MetricAsync`.

→ [`recipes/multi-agent-budget/`](../recipes/multi-agent-budget/)

---

### email-vendor-leases

**Lease-scoped vendor credential delegation.**

A customer-facing orchestrator issues short-lived provisioned credentials for
an email-sending service. The agent uses the credential to send one batch and
then the credential is revoked on job completion.

Concepts: `CredentialProvisioner`, `ICredentialStore`, `ctx.RotateCredentialAsync`,
`LeaseNamespaces.NetFetch`, `model.use` glob enforcement.

→ [`recipes/email-vendor-leases/`](../recipes/email-vendor-leases/)

---

### stream-resume

**Streamed results with reconnect after a transport drop.**

An agent emits a large report as `result_chunk` events. The client
intentionally drops the WebSocket connection mid-stream and reconnects using
its `resume_token`. The runtime replays the missed chunks gap-free.

Concepts: `ctx.BeginResultStream`, `ctx.WriteChunkAsync`, `handle.Chunks`,
`ArcpClient.ResumeToken`, `RESUME_WINDOW_EXPIRED`.

→ [`recipes/stream-resume/`](../recipes/stream-resume/)

---

### mcp-skill

**MCP bridge — exposing an ARCP agent as an MCP tool.**

A thin adapter wraps an `ArcpClient` so that an MCP host can call ARCP agents
as ordinary MCP tools. Each MCP tool call maps to one `SubmitAsync`, streams
`log` events as MCP progress notifications, and returns the `job.result`
payload as the MCP tool result.

Concepts: `SubmitAsync`, `handle.Events`, vendor extension round-trip,
stdio transport for subprocess hosting.

→ [`recipes/mcp-skill/`](../recipes/mcp-skill/)
