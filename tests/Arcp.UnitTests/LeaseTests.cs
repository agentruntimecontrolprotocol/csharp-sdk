// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Text.Json;
using Arcp.Core.Leases;
using Arcp.Core.Wire;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class LeaseTests
{
    [Fact]
    public void Lease_default_ctor_has_empty_capabilities()
    {
        var lease = new Lease();
        lease.Capabilities.Should().BeEmpty();
        lease.Get(LeaseNamespaces.FsRead).Should().BeEmpty();
    }

    [Fact]
    public void Lease_dictionary_ctor_copies_capabilities()
    {
        var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.FsRead] = new[] { "**/*.md" },
            [LeaseNamespaces.NetFetch] = new[] { "https://example.com/*" },
        });
        lease.Get(LeaseNamespaces.FsRead).Should().ContainSingle().Which.Should().Be("**/*.md");
        lease.Get(LeaseNamespaces.NetFetch).Should().ContainSingle();
        lease.Get("unknown.namespace").Should().BeEmpty();
    }

    [Fact]
    public void Lease_round_trips_through_arcp_json()
    {
        var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.FsRead] = new[] { "src/**" },
            [LeaseNamespaces.CostBudget] = new[] { "USD:5.00" },
        });
        var json = JsonSerializer.Serialize(lease, ArcpJson.Options);
        json.Should().Contain("fs.read");
        json.Should().Contain("USD:5.00");
        var roundtrip = JsonSerializer.Deserialize<Lease>(json, ArcpJson.Options)!;
        roundtrip.Get(LeaseNamespaces.FsRead).Should().Equal("src/**");
        roundtrip.Get(LeaseNamespaces.CostBudget).Should().Equal("USD:5.00");
    }

    [Fact]
    public void BudgetAmount_parse_round_trips()
    {
        var ok = BudgetAmount.TryParse("USD:1.50", out var amt);
        ok.Should().BeTrue();
        amt.Currency.Should().Be("USD");
        amt.Amount.Should().Be(1.50m);
        amt.ToString().Should().StartWith("USD:1.5");
    }

    [Fact]
    public void BudgetAmount_parse_rejects_invalid_input()
    {
        BudgetAmount.TryParse("", out _).Should().BeFalse();
        BudgetAmount.TryParse(null, out _).Should().BeFalse();
        BudgetAmount.TryParse("USD", out _).Should().BeFalse();
        BudgetAmount.TryParse(":1.00", out _).Should().BeFalse();
        BudgetAmount.TryParse("USD:", out _).Should().BeFalse();
        BudgetAmount.TryParse("USD:not-a-number", out _).Should().BeFalse();
        BudgetAmount.TryParse("BAD$:1.00", out _).Should().BeFalse();
        BudgetAmount.TryParse("USD:-1.00", out _).Should().BeFalse();
    }

    [Fact]
    public void BudgetAmount_parse_throws_on_invalid()
    {
        var act = () => BudgetAmount.Parse("not a budget");
        act.Should().Throw<FormatException>();
    }
}
