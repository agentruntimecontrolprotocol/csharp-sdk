// Upstream MCP server invocation. Real version parameterizes command/args/env
// via your config layer. The official .NET MCP SDK lives at
// https://github.com/modelcontextprotocol/csharp-sdk (NuGet:
// `ModelContextProtocol`). Stubbed here so the sample stays focused on the
// §20 translation between protocols.
namespace ARCP.Samples.MCP;

internal sealed record StdioServerParameters(string Command, IReadOnlyList<string> Args);

internal sealed class McpClientSession : IAsyncDisposable
{
    public Task InitializeAsync() => throw new NotImplementedException();

    public Task<McpToolList> ListToolsAsync() => throw new NotImplementedException();

    public Task<McpCallToolResult> CallToolAsync(string tool, IReadOnlyDictionary<string, object> arguments) =>
        throw new NotImplementedException();

    public ValueTask DisposeAsync() => throw new NotImplementedException();
}

internal sealed record McpTool(string Name);

internal sealed record McpToolList(IReadOnlyList<McpTool> Tools);

internal sealed record McpContent(string? Text);

internal sealed record McpCallToolResult(IReadOnlyList<McpContent> Content, bool IsError);

internal static class McpStdio
{
    public static Task<McpClientSession> ConnectAsync(StdioServerParameters parameters) =>
        throw new NotImplementedException();
}

internal static class Upstream
{
    public static StdioServerParameters Params() =>
        new(Command: "uvx", Args: new[] { "mcp-server-filesystem", "/srv/data" });
}
