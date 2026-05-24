// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
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
}
