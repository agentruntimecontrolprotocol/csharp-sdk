// SPDX-License-Identifier: Apache-2.0
// samples/ListJobs: paginated read-only inventory of jobs in this session. Spec §6.6.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "list-jobs", Version = "1.0.0" },
});
server.RegisterAgent("worker", async (ctx, ct) =>
{
    await Task.Delay(200, ct);
    return null;
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "list-client", Version = "1.0.0" },
});
for (var i = 0; i < 3; i++) await client.SubmitAsync("worker");
var page = await client.ListJobsAsync(filter: new JobListFilter { Status = new[] { "running", "pending" } });
foreach (var job in page.Jobs)
{
    Console.WriteLine($"  {job.JobId} agent={job.Agent} status={job.Status}");
}
return 0;
