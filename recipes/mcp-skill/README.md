# Recipe: mcp-skill

Shows how to expose an ARCP agent as an MCP (Model Context Protocol) skill so
that an AI assistant can invoke it as a tool.  A single long-lived `ArcpClient`
is shared across all invocations to amortise connection overhead.  Each MCP
tool call maps to one ARCP job submission; log events are forwarded to the MCP
caller as progress notifications.

See [`skills/research/SKILL.md`](./skills/research/SKILL.md) for the MCP skill
manifest that Claude Code uses to discover and invoke the skill.

## What this demonstrates

| Feature | Spec ref |
| ------- | -------- |
| Long-lived `ArcpClient` shared across multiple `SubmitAsync` calls | §4 |
| `handle.Events()` — consuming `log` events as progress notifications | §12.1 |
| `ctx.LogAsync` — agent-side progress emission | §12.1 |
| MCP ↔ ARCP adapter pattern | — |

## Architecture

```
AI assistant
    │  MCP tool call: research(topic=...)
    ▼
HandleResearchToolCall()          ← MCP adapter
    │  SubmitAsync("research", { topic })
    ▼
ArcpClient (persistent session)
    │  job.submit → job.log → job.result
    ▼
ArcpServer / "research" agent
```

`HandleResearchToolCall` is the thin adapter layer.  In a production MCP
server it would be the function body registered with your MCP library's tool
handler.  It:

1. Submits an ARCP job for the requested topic.
2. Spawns a background consumer that forwards `log` events as MCP progress
   notifications.
3. Awaits `handle.Result` and returns the consolidated outcome string.

## Run

```sh
dotnet run --project recipes/mcp-skill
```

## Extending to a real MCP server

Replace the `foreach (var topic in topics)` loop with your MCP library's tool
registration, e.g. (pseudocode):

```csharp
mcpServer.AddTool("research", async (args, ct) =>
    await HandleResearchToolCall(args["topic"].GetString()!, ct));
```

## Related

- [MCP skill manifest](./skills/research/SKILL.md)
- [Job events guide](../../docs/guides/job-events.md)
- [`samples/SubmitAndStream`](../../samples/SubmitAndStream/) — event streaming basics
