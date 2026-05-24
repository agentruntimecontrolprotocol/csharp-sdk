// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Runtime.Budget;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class BudgetLedgerTests
{
    [Fact]
    public void Apply_metric_charges_matching_currency()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:2.00" },
        }));
        ledger.ApplyMetric("cost.inference", 0.50, "USD").Should().BeTrue();
        ledger.Remaining["USD"].Should().Be(1.50m);
    }

    [Fact]
    public void Apply_metric_with_unknown_currency_is_noop()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        }));
        ledger.ApplyMetric("cost.inference", 0.50, "EUR").Should().BeFalse();
        ledger.Remaining["USD"].Should().Be(1.00m);
    }

    [Fact]
    public void Apply_metric_with_negative_value_is_noop()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        }));
        ledger.ApplyMetric("cost.inference", -1.0, "USD").Should().BeFalse();
    }

    [Fact]
    public void Apply_metric_with_non_cost_namespace_is_noop()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        }));
        ledger.ApplyMetric("perf.latency", 1.0, "USD").Should().BeFalse();
    }

    [Fact]
    public void AssertNotExhausted_throws_when_balance_hits_zero()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        }));
        ledger.ApplyMetric("cost.inference", 1.00, "USD");
        var act = ledger.AssertNotExhausted;
        act.Should().Throw<BudgetExhaustedException>();
    }

    [Fact]
    public void IsExhausted_tracks_specific_currency()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00", "EUR:5.00" },
        }));
        ledger.ApplyMetric("cost.inference", 1.00, "USD");
        ledger.IsExhausted("USD").Should().BeTrue();
        ledger.IsExhausted("EUR").Should().BeFalse();
    }

    [Fact]
    public void Cost_budget_remaining_metric_is_excluded_from_charge()
    {
        var ledger = new BudgetLedger();
        ledger.Initialize(new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        }));
        ledger.ApplyMetric("cost.budget.remaining", 0.5, "USD").Should().BeFalse();
        ledger.Remaining["USD"].Should().Be(1.00m);
    }
}
