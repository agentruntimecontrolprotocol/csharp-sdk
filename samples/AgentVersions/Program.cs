// SPDX-License-Identifier: Apache-2.0
// samples/AgentVersions: register multiple versions, pick one with name@version, observe default
// resolution and AGENT_VERSION_NOT_AVAILABLE for unknown versions. Spec §7.5, §12, §13.7.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "agent-versions", Version = "1.0.0" },
});
server.RegisterAgentVersion("code-refactor", "1.0.0", (ctx, ct) => Task.FromResult<object?>("v1"));
server.RegisterAgentVersion("code-refactor", "2.0.0", (ctx, ct) => Task.FromResult<object?>("v2"));
server.SetDefaultAgentVersion("code-refactor", "2.0.0");

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "versions-client", Version = "1.0.0" },
});
foreach (var a in client.Agents)
{
    Console.WriteLine($"inventory: {a.Name} versions=[{string.Join(",", a.Versions ?? Array.Empty<string>())}] default={a.Default}");
}
var bare = await client.SubmitAsync("code-refactor");
Console.WriteLine($"bare resolved to: {bare.Agent}");
var pinned = await client.SubmitAsync("code-refactor@1.0.0");
Console.WriteLine($"pinned: {pinned.Agent}");
await Task.WhenAll(bare.Result, pinned.Result);
return 0;
