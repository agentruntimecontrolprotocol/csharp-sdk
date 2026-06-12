// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Runtime.Agents;
using Arcp.Runtime.Budget;
using Arcp.Runtime.Leases;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class AuditFixesUnitTests
{
    [Fact]
    public void AuthorizeOperation_fails_with_BUDGET_EXHAUSTED_once_a_counter_hits_zero()
    {
        // Spec §9.6: budget counters are a pre-operation gate. Once any counter is ≤ 0, the next
        // lease-authorized operation MUST fail with BUDGET_EXHAUSTED via the enforcement path —
        // not only reactively from a cost metric.
        var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
            [LeaseNamespaces.ToolCall] = new[] { "*" },
        });
        var manager = new LeaseManager();

        var fresh = new BudgetLedger();
        fresh.Initialize(lease);
        var ok = () => manager.AuthorizeOperation(lease, null, LeaseNamespaces.ToolCall, "search.web", fresh);
        ok.Should().NotThrow("budget is not yet exhausted");

        var exhausted = new BudgetLedger();
        exhausted.Initialize(lease);
        exhausted.ApplyMetric("cost.search", 1.00, "USD"); // remaining → 0
        var act = () => manager.AuthorizeOperation(lease, null, LeaseNamespaces.ToolCall, "search.web", exhausted);
        act.Should().Throw<BudgetExhaustedException>();
    }

    [Fact]
    public async Task AgentRegistry_RegisterVersion_and_ToInventory_are_concurrency_safe()
    {
        // Spec §7.5 hygiene: ToInventory must obtain a consistent per-agent snapshot without
        // enumerating mutable state that a concurrent RegisterVersion is writing.
        var registry = new AgentRegistry();
        registry.Register("triage", new DelegateAgent((_, _) => Task.FromResult<object?>(null)));

        using var cts = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            var n = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                registry.RegisterVersion("triage", $"1.0.{n++}", new DelegateAgent((_, _) => Task.FromResult<object?>(null)));
            }
        });

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++)
            {
                var inventory = registry.ToInventory();
                inventory.Should().NotBeNull();
            }
        });

        await reader; // must complete without InvalidOperationException from concurrent mutation
        cts.Cancel();
        await writer;
    }
}
