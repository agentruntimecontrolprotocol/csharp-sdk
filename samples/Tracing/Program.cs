// SPDX-License-Identifier: Apache-2.0
// samples/Tracing: wrap the transport with ActivitySource instrumentation and observe spans.
// Spec §11.
using System.Diagnostics;
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Tracing;
using Arcp.Core.Transport;
using Arcp.Otel;
using Arcp.Runtime;

ActivitySource.AddActivityListener(new ActivityListener
{
    ShouldListenTo = src => src.Name == ArcpDiagnostics.TransportSourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStarted = a => Console.WriteLine($"  span START: {a.OperationName}"),
    ActivityStopped = a => Console.WriteLine($"  span STOP:  {a.OperationName} ({a.Duration.TotalMilliseconds:F1}ms)"),
});

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "tracing", Version = "1.0.0" },
});
server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input));

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT.WithTracing());
await using var client = await ArcpClient.ConnectAsync(clientT.WithTracing(), new ArcpClientOptions
{
    Client = new ClientInfo { Name = "tracing-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("echo", "traced");
await handle.Result;
return 0;
