// SPDX-License-Identifier: Apache-2.0
// samples/IdempotentRetry: two submits with the same idempotency_key resolve to one job.
// Spec: §7.2.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "idempotent-retry", Version = "1.0.0" },
});
server.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>("ok"));

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "idem-client", Version = "1.0.0" },
});
var h1 = await client.SubmitAsync("noop", idempotencyKey: "refactor-2026-W19");
var h2 = await client.SubmitAsync("noop", idempotencyKey: "refactor-2026-W19");
Console.WriteLine($"first  job_id={h1.JobId}");
Console.WriteLine($"second job_id={h2.JobId}");
Console.WriteLine($"same job? {h1.JobId == h2.JobId}");
await h1.Result;
return h1.JobId == h2.JobId ? 0 : 1;
