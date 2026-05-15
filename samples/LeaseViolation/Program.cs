// SPDX-License-Identifier: Apache-2.0
// samples/LeaseViolation: an operation outside the lease surfaces a tool_result.error with code
// PERMISSION_DENIED; the job continues. Spec: §9.3, §12.
using Arcp.Client;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "lease-violation", Version = "1.0.0" },
});
server.RegisterAgent("scanner", async (ctx, ct) =>
{
    var lm = new Arcp.Runtime.Leases.LeaseManager();
    try
    {
        lm.AuthorizeOperation(ctx.Lease, ctx.LeaseConstraints, LeaseNamespaces.FsRead, "/etc/passwd");
    }
    catch (PermissionDeniedException ex)
    {
        await ctx.ToolResultAsync("read_etc_passwd", null, new ToolError
        {
            Code = ex.Code,
            Message = ex.Message,
        });
    }
    return "scan-complete";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "lease-client", Version = "1.0.0" },
});
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    ["fs.read"] = new[] { "/workspace/**" },
});
var handle = await client.SubmitAsync("scanner", leaseRequest: lease);
var result = await handle.Result;
Console.WriteLine($"scan: {result.FinalStatus}");
return 0;
