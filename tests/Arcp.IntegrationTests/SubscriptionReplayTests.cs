// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class SubscriptionReplayTests
{
    [Fact]
    public async Task Subscriber_with_history_true_receives_prior_events_before_live()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });

        var firstStepDone = new TaskCompletionSource();
        var continueGate = new TaskCompletionSource();

        server.RegisterAgent("two-steppy", async (ctx, ct) =>
        {
            await ctx.LogAsync("info", "step-1", ct);
            await ctx.LogAsync("info", "step-2", ct);
            firstStepDone.SetResult();
            await continueGate.Task;
            await ctx.LogAsync("info", "step-3", ct);
            return "done";
        });

        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var owner = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "owner", Version = "1" },
        });
        var handle = await owner.SubmitAsync("two-steppy");

        // Wait until 2 events have been emitted server-side.
        await firstStepDone.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var (sub2T, srv2) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv2));

        await using var watcher = await ArcpClient.ConnectAsync(sub2T, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "watcher", Version = "1" },
            Features = new[] { FeatureFlags.Subscribe },
        });

        var sub = await watcher.SubscribeAsync(handle.JobId, history: true);
        // Release the agent so it emits step-3.
        continueGate.SetResult();

        var received = new List<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var ev in sub.Events(cts.Token))
        {
            if (ev.Kind == "log")
            {
                var body = ev.BodyAs<LogBody>();
                if (body is not null) received.Add(body.Message);
            }
            if (received.Count >= 3) break;
        }

        received.Should().HaveCount(3);
        received[0].Should().Be("step-1");
        received[1].Should().Be("step-2");
        received[2].Should().Be("step-3");
    }
}
