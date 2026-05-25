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
    public async Task Budget_exhaustion_emits_non_fatal_tool_result_error_and_agent_finishes_normally()
    {
        // Spec §9.6 SHOULD: prefer surfacing exhaustion as a `tool_result.error` so the agent
        // may decide whether to continue with non-cost-bearing operations.
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        server.RegisterAgent("spender", async (ctx, ct) =>
        {
            await ctx.MetricAsync("cost.inference", 1.50, unit: "USD", cancellationToken: ct);
            // Agent may continue with non-cost-bearing work and emit a partial result.
            await ctx.LogAsync("info", "exhausted; returning partial", ct);
            return "partial";
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

        // Drain events on the foreground; the channel completes once the terminal envelope arrives.
        var sawBudgetExhausted = false;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var ev in handle.Events(cts.Token))
        {
            if (ev.Kind != "tool_result") continue;
            var body = ev.BodyAs<ToolResultBody>();
            if (body?.Error?.Code == ErrorCode.BudgetExhausted) sawBudgetExhausted = true;
        }

        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));

        result.Success.Should().BeTrue();
        sawBudgetExhausted.Should().BeTrue(
            because: "spec §9.6 SHOULD: exhaustion surfaces as a tool_result.error so the agent can continue");
    }

    [Fact]
    public async Task FatalBudgetExhaustion_option_restores_legacy_terminal_BUDGET_EXHAUSTED_behavior()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            FatalBudgetExhaustion = true,
        });
        server.RegisterAgent("spender", async (ctx, ct) =>
        {
            await ctx.MetricAsync("cost.inference", 1.50, unit: "USD", cancellationToken: ct);
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
