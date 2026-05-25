// SPDX-License-Identifier: Apache-2.0
// samples/LeaseExpiresAt: a short-lived lease expires while the agent is running; the runtime
// emits a status{phase:"lease_expired"} event and then job.error{LEASE_EXPIRED,
// final_status:"error"}. Spec §9.5.
using Arcp.Client;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "lease-expires-at", Version = "1.0.0" },
});
server.RegisterAgent("worker", async (ctx, ct) =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
    catch (OperationCanceledException)
    {
        await ctx.LogAsync("warn", "cancelled due to lease expiry", CancellationToken.None);
        throw;
    }
    return "completed";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "lease-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("worker", leaseConstraints: new LeaseConstraints
{
    ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(2),
});
var res = await handle.Result;
Console.WriteLine($"job ended: status={res.FinalStatus} code={res.Error?.Code}");
return 0;
