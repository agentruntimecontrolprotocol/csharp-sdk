// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class RuntimeLimitTests
{
    [Fact]
    public async Task Job_exceeding_max_runtime_sec_terminates_with_timeout()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        server.RegisterAgent("forever", async (ctx, ct) =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return null;
        });
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(client, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });

        var handle = await c.SubmitAsync("forever", maxRuntimeSec: 1);
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));

        result.Success.Should().BeFalse();
        result.Error!.FinalStatus.Should().Be("timed_out");
        result.Error.Code.Should().Be(ErrorCode.Timeout);
        result.Error.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Job_finishing_before_lease_expiry_does_not_emit_late_lease_expired()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        server.RegisterAgent("instant", (ctx, ct) => Task.FromResult<object?>("done"));
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(client, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });

        var leaseConstraints = new Arcp.Core.Leases.LeaseConstraints
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(10),
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var handle = await c.SubmitAsync("instant", leaseConstraints: leaseConstraints);
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));
        sw.Stop();

        result.Success.Should().BeTrue();
        result.Result!.FinalStatus.Should().Be("success");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
            because: "the watchdog must not keep the run task alive until lease expiry");
    }
}
