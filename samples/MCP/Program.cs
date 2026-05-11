// ARCP runtime fronting an MCP server (RFC §20).
//
// MCP describes capabilities; ARCP operationalizes them. This bridge
// translates inbound ARCP `tool.invoke` envelopes into MCP `call_tool` calls
// against an upstream MCP server, and emits the ARCP job lifecycle back to
// the calling client.
//
//   ARCP client ──tool.invoke──> bridge ──call_tool──> MCP server
//   ARCP client <─job.{accepted,started,completed,failed}─ bridge
using System.Text.Json;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Execution;
using ARCP.Samples.MCP;

// Per RFC §20:
//   MCP tool schema -> ARCP capability  (advertised at session.accepted)
//   MCP tool call   -> ARCP job
//   MCP resource    -> ARCP stream of kind: event  (delegated to MCP)

static async Task<IReadOnlyList<string>> AdvertiseFromMcpAsync(McpClientSession mcp)
{
    // MCP `tools/list` → namespaced ARCP capability extensions. Each
    // upstream tool surfaces as `arcpx.mcp.tool.<name>.v1` so clients can
    // negotiate which tools they require at session open.
    McpToolList listed = await mcp.ListToolsAsync();
    return listed.Tools.Select(t => $"arcpx.mcp.tool.{t.Name}.v1").ToList();
}

static async Task<JsonElement> CallViaMcpAsync(
    McpClientSession mcp,
    string tool,
    IReadOnlyDictionary<string, object> arguments)
{
    // Translate ARCP `tool.invoke.payload` into MCP `call_tool`. MCP returns
    // typed content blocks; we flatten to a JSON-serializable dict for the
    // ARCP `tool.result` / `job.completed` payload. MCP errors become
    // canonical ARCP error codes.
    McpCallToolResult result;
    try
    {
        result = await mcp.CallToolAsync(tool, arguments);
    }
    catch (Exception exc)
    {
        throw new ARCPException(ErrorCode.Internal, exc.Message, cause: exc);
    }

    if (result.IsError)
    {
        string text = string.Join("\n", result.Content.Select(c => c.Text ?? string.Empty));
        // MCP doesn't carry a typed error code; FAILED_PRECONDITION is the
        // right canonical mapping for "tool ran, said no".
        throw new ARCPException(ErrorCode.FailedPrecondition, text.Length > 0 ? text : "tool error");
    }

    return JsonSerializer.SerializeToElement(new
    {
        content = result.Content.Select(c => new { text = c.Text }).ToArray(),
    });
}

static async Task HandleInvokeAsync(Func<Envelope, Task> send, McpClientSession mcp, Envelope request)
{
    // One inbound ARCP `tool.invoke` → MCP call → ARCP job lifecycle.
    JobId jobId = new($"job_{Guid.NewGuid():N}"[..14]);

    await send(new Envelope
    {
        Arcp = ARCP.ProtocolVersion.Wire,
        Id = MessageId.New(),
        Type = "job.accepted",
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = request.Id,
        JobId = jobId,
        Payload = new JobAccepted(jobId, DateTimeOffset.UtcNow),
    });
    await send(new Envelope
    {
        Arcp = ARCP.ProtocolVersion.Wire,
        Id = MessageId.New(),
        Type = "job.started",
        Timestamp = DateTimeOffset.UtcNow,
        JobId = jobId,
        Payload = new JobStarted(jobId, DateTimeOffset.UtcNow),
    });

    JsonElement result;
    try
    {
        ToolInvoke invoke = (ToolInvoke)request.Payload;
        Dictionary<string, object> arguments = invoke.Arguments is { } args
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(args) ?? []
            : [];
        result = await CallViaMcpAsync(mcp, invoke.Tool, arguments);
    }
    catch (ARCPException exc)
    {
        await send(new Envelope
        {
            Arcp = ARCP.ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "job.failed",
            Timestamp = DateTimeOffset.UtcNow,
            JobId = jobId,
            Payload = new JobFailed(exc.Code, exc.Message),
        });
        return;
    }

    await send(new Envelope
    {
        Arcp = ARCP.ProtocolVersion.Wire,
        Id = MessageId.New(),
        Type = "job.completed",
        Timestamp = DateTimeOffset.UtcNow,
        JobId = jobId,
        Payload = new JobCompleted(Result: result),
    });
}

static async Task RunBridgeAsync(Func<Envelope, Task> send, IAsyncEnumerable<Envelope> inbound)
{
    // Wire one MCP session as the upstream for one ARCP runtime.
    await using McpClientSession mcp = await McpStdio.ConnectAsync(Upstream.Params());
    await mcp.InitializeAsync();
    IReadOnlyList<string> extensions = await AdvertiseFromMcpAsync(mcp);
    // In production this list would feed `Capabilities.Extensions` at the
    // runtime's `session.accepted` so clients negotiate exactly the MCP
    // tools they expect to use.
    Console.WriteLine($"bridged: [{string.Join(", ", extensions)}]");

    await foreach (Envelope envelope in inbound)
    {
        if (envelope.Type == "tool.invoke")
        {
            await HandleInvokeAsync(send, mcp, envelope);
        }
    }
}

// Production version: instantiate an `ARCPRuntime`, point its tool-invoke
// handler at `HandleInvokeAsync`, and let the WebSocket transport carry
// inbound envelopes from real ARCP clients. We elide the runtime wiring
// (symmetric with `ARCP.Runtime.ARCPRuntime`) so this file stays focused on
// the §20 translation between protocols.
Func<Envelope, Task> send = null!;             // bound to the runtime's outbound channel
IAsyncEnumerable<Envelope> inbound = null!;    // async iterator of inbound envelopes
await RunBridgeAsync(send, inbound);
