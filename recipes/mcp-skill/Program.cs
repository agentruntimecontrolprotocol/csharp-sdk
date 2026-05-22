// SPDX-License-Identifier: Apache-2.0
// recipes/mcp-skill: shows how to expose an ARCP agent as an MCP skill.  A single
// long-lived ArcpClient is shared across all invocations so each MCP tool call
// avoids the overhead of a new connection.  HandleResearchToolCall mirrors what an
// MCP server adapter would do: submit a job, stream log events back to the caller
// as progress notifications, and return the consolidated result.
// See skills/research/SKILL.md for the MCP skill manifest.  Spec §4, §12.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

using var cts = new CancellationTokenSource();
var ct = cts.Token;

// ── ARCP server ────────────────────────────────────────────────────────────────
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "mcp-skill", Version = "1.0.0" },
});

server.RegisterAgent("research", async (ctx, rct) =>
{
    var topic = ctx.Input.TryGetProperty("topic", out var t)
        ? t.GetString() ?? "unknown"
        : "unknown";

    await ctx.LogAsync("info", $"searching knowledge base for: {topic}", rct);
    await Task.Delay(80, rct);
    await ctx.LogAsync("info", "synthesising results…", rct);
    await Task.Delay(40, rct);

    return new { summary = $"Research on '{topic}': 3 key findings identified." };
});

// ── long-lived ARCP client ────────────────────────────────────────────────────
// One persistent session serves all MCP tool calls.  This mirrors how a real
// MCP server adapter would hold a connection to the ARCP runtime rather than
// reconnecting on every invocation.
var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT, ct);
await using var arcpClient = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "mcp-skill-host", Version = "1.0.0" },
});

// ── MCP skill adapter ─────────────────────────────────────────────────────────
// In production this function body would live inside the MCP tool handler
// registered with your MCP server library.
async Task<string> HandleResearchToolCall(string topic, CancellationToken cancellationToken)
{
    Console.WriteLine($"[mcp] tool call → research(topic=\"{topic}\")");

    var handle = await arcpClient.SubmitAsync(
        "research",
        input: new { topic },
        cancellationToken: cancellationToken);

    // Stream ARCP log events to the simulated MCP caller as progress updates.
    // A real adapter would forward these as MCP progress notifications.
    _ = Task.Run(async () =>
    {
        await foreach (var ev in handle.Events())
        {
            if (ev.Kind == "log")
                Console.WriteLine($"  [arcp log] {ev.Body.GetRawText()}");
        }
    }, cancellationToken);

    var result = await handle.Result;
    var outcome = result.Success
        ? $"Research on '{topic}' completed successfully."
        : $"Research on '{topic}' failed.";

    Console.WriteLine($"[mcp] tool result → {outcome}");
    return outcome;
}

// ── simulate an AI assistant issuing sequential MCP tool calls ────────────────
// In a real deployment the MCP server would call HandleResearchToolCall each time
// the AI assistant invokes the "research" skill tool.
var topics = new[]
{
    "transformer architecture",
    "retrieval-augmented generation",
    "ARCP wire protocol",
};

foreach (var topic in topics)
    await HandleResearchToolCall(topic, ct);

return 0;
