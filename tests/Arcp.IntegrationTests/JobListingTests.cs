// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class JobListingTests
{
    private static (ArcpServer server, MemoryTransport clientT) StartServer(Action<ArcpServer> configure)
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        configure(server);
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        return (server, client);
    }

    [Fact]
    public async Task ListJobsAsync_filter_by_agent_returns_only_matching()
    {
        var (_, transport) = StartServer(s =>
        {
            s.RegisterAgent("a", async (ctx, ct) => { await Task.Delay(200, ct); return null; });
            s.RegisterAgent("b", async (ctx, ct) => { await Task.Delay(200, ct); return null; });
        });
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = new[] { FeatureFlags.ListJobs },
        });
        await c.SubmitAsync("a");
        await c.SubmitAsync("a");
        await c.SubmitAsync("b");
        var page = await c.ListJobsAsync(new JobListFilter { Agent = "a" });
        page.Jobs.Should().HaveCount(2);
        page.Jobs.All(j => j.Agent == "a").Should().BeTrue();
    }

    [Fact]
    public async Task ListJobsAsync_filter_by_status_filters_results()
    {
        var (_, transport) = StartServer(s =>
        {
            s.RegisterAgent("instant", (ctx, ct) => Task.FromResult<object?>("done"));
            s.RegisterAgent("slow", async (ctx, ct) => { await Task.Delay(500, ct); return null; });
        });
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = new[] { FeatureFlags.ListJobs },
        });
        var finished = await c.SubmitAsync("instant");
        await finished.Result.WaitAsync(TimeSpan.FromSeconds(2));
        await c.SubmitAsync("slow");
        var successOnly = await c.ListJobsAsync(new JobListFilter { Status = new[] { "success" } });
        successOnly.Jobs.All(j => j.Status == "success").Should().BeTrue();
    }

    [Fact]
    public async Task ListJobsAsync_filter_by_created_after_excludes_older_jobs()
    {
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("noop", async (ctx, ct) => { await Task.Delay(200, ct); return null; }));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = new[] { FeatureFlags.ListJobs },
        });
        await c.SubmitAsync("noop");
        var future = await c.ListJobsAsync(new JobListFilter { CreatedAfter = DateTimeOffset.UtcNow.AddHours(1) });
        future.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task ListJobsAsync_paginates_via_cursor()
    {
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("noop", async (ctx, ct) => { await Task.Delay(200, ct); return null; }));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = new[] { FeatureFlags.ListJobs },
        });
        for (int i = 0; i < 3; i++) await c.SubmitAsync("noop");
        var firstPage = await c.ListJobsAsync(limit: 2);
        firstPage.Jobs.Should().HaveCount(2);
        firstPage.NextCursor.Should().NotBeNullOrEmpty();
        var secondPage = await c.ListJobsAsync(limit: 2, cursor: firstPage.NextCursor);
        secondPage.Jobs.Should().HaveCount(1);
        secondPage.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListJobsAsync_throws_when_server_returns_session_error()
    {
        // A list_jobs request rejected by the server (here: feature not negotiated → INVALID_REQUEST)
        // emits a session.error. The awaiting ListJobsAsync MUST throw, not hang until cancellation.
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>(null)));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            // list_jobs feature intentionally NOT negotiated → server rejects with INVALID_REQUEST.
            Features = Array.Empty<string>(),
        });

        var act = async () => await c.ListJobsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await act.Should().ThrowAsync<ArcpException>()
            .Where(e => e.Code == ErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task ListJobsAsync_reports_last_event_seq_for_jobs_that_emitted_events()
    {
        // Spec §6.6: each listed job carries last_event_seq so a dashboard knows where to subscribe
        // from. A running job that has emitted events MUST report a non-null, monotonic value.
        var gate = new TaskCompletionSource();
        var (_, transport) = StartServer(s => s.RegisterAgent("emitter", async (ctx, ct) =>
        {
            await ctx.StatusAsync("phase-1", "working", ct);
            await ctx.ProgressAsync(1, 10, "items", null, ct);
            gate.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return null;
        }));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = new[] { FeatureFlags.ListJobs },
        });

        await c.SubmitAsync("emitter");
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var page = await c.ListJobsAsync();
        var entry = page.Jobs.Should().ContainSingle().Subject;
        entry.LastEventSeq.Should().NotBeNull();
        entry.LastEventSeq.Should().BeGreaterThan(0);
    }
}
