// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class BudgetEnforcementTests
{
    [Fact]
    public async Task Job_emitting_metric_that_exhausts_cost_budget_terminates_with_BUDGET_EXHAUSTED()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        server.RegisterAgent("spender", async (ctx, ct) =>
        {
            await ctx.MetricAsync("cost.inference", 1.50, unit: "USD", cancellationToken: ct);
            // Should not reach here — the metric exhausts USD budget.
            await Task.Delay(500, ct);
            return "should-not-complete";
        });
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(client, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });

        var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        });
        var handle = await c.SubmitAsync("spender", leaseRequest: lease);
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.BudgetExhausted);
        result.Error.Retryable.Should().BeFalse();
    }
}
