// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
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

public class SubscriptionOrderingTests
{
    // ── #39: under concurrent emitters in one session, delivered event_seq is strictly increasing ──
    [Fact]
    public async Task Concurrent_emitters_produce_strictly_monotonic_event_seq()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        const int writers = 4;
        const int perWriter = 25;
        server.RegisterAgent("fanout", async (ctx, ct) =>
        {
            var tasks = new List<Task>();
            for (var k = 0; k < writers; k++)
            {
                var id = k;
                tasks.Add(Task.Run(async () =>
                {
                    for (var i = 0; i < perWriter; i++)
                        await ctx.LogAsync("info", $"{id}-{i}", ct);
                }, ct));
            }
            await Task.WhenAll(tasks);
            return "done";
        });
        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });
        var handle = await c.SubmitAsync("fanout");

        var seqs = new List<long>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var ev in handle.Events(cts.Token))
        {
            if (ev.Kind == "log") seqs.Add(ev.EventSeq);
            if (seqs.Count >= writers * perWriter) break;
        }

        seqs.Should().HaveCount(writers * perWriter);
        for (var i = 1; i < seqs.Count; i++)
            seqs[i].Should().BeGreaterThan(seqs[i - 1], "event_seq must be strictly increasing with no reordering (spec §8.3)");
    }

    // ── #44: a subscriber attaching during concurrent emission sees each event exactly once, ordered ──
    [Fact]
    public async Task Subscriber_attaching_mid_stream_sees_every_event_exactly_once()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });

        const int total = 40;
        var subscribeNow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RegisterAgent("emitter", async (ctx, ct) =>
        {
            for (var i = 1; i <= total; i++)
            {
                await ctx.LogAsync("info", i.ToString(), ct);
                if (i == 5) subscribeNow.TrySetResult(); // let a subscriber attach mid-stream
                await Task.Delay(3, ct);
            }
            return "done";
        });

        var (ownerT, ownerSrv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(ownerSrv));
        await using var owner = await ArcpClient.ConnectAsync(ownerT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "owner", Version = "1" },
        });
        var handle = await owner.SubmitAsync("emitter");

        await subscribeNow.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var (watchT, watchSrv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(watchSrv));
        await using var watcher = await ArcpClient.ConnectAsync(watchT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "watcher", Version = "1" },
            Features = new[] { FeatureFlags.Subscribe },
        });

        var sub = await watcher.SubscribeAsync(handle.JobId, history: true);

        var received = new List<int>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var ev in sub.Events(cts.Token))
        {
            if (ev.Kind == "log")
            {
                received.Add(int.Parse(ev.BodyAs<LogBody>()!.Message));
                if (received.Count >= total) break;
            }
        }

        // Exactly once, in order, with no gap or duplicate at the subscribe boundary (spec §7.6).
        received.Should().HaveCount(total);
        received.Distinct().Should().HaveCount(total, "no event may be duplicated at the subscribe boundary");
        received.Should().BeInAscendingOrder("events must arrive in order");
        received.Should().Equal(Enumerable.Range(1, total), "no event may be lost at the subscribe boundary");
    }
}
