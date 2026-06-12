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

    [Fact]
    public void GlobMatch_is_permissive_for_double_star_and_strict_otherwise()
    {
        // Anchor the runtime's lease-gate behavior: when a lease declares tool.call:["calc.*"],
        // ctx.ToolCallAsync("calc.add", ...) is allowed but ctx.ToolCallAsync("fs.write", ...)
        // raises PermissionDenied (spec §9.3).
        var manager = new Arcp.Runtime.Leases.LeaseManager();
        var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.ToolCall] = new[] { "calc.*" },
        });

        var ok = () => manager.AuthorizeOperation(lease, null, LeaseNamespaces.ToolCall, "calc.add");
        var bad = () => manager.AuthorizeOperation(lease, null, LeaseNamespaces.ToolCall, "fs.write");

        ok.Should().NotThrow();
        bad.Should().Throw<Arcp.Core.Errors.PermissionDeniedException>();
    }

    [Fact]
    public void GlobMatch_double_star_respects_the_path_boundary()
    {
        // Spec §9.2/§9.3: a "/prefix/**" grant must not authorize sibling paths that merely
        // share the string prefix. The trailing separator is the enforcement boundary.
        Arcp.Runtime.Leases.LeaseManager.GlobMatch("/workspace/myapp/src/a.cs", "/workspace/myapp/**")
            .Should().BeTrue();
        Arcp.Runtime.Leases.LeaseManager.GlobMatch("/workspace/myapp/a", "/workspace/myapp/**")
            .Should().BeTrue();
        // The directory itself is covered.
        Arcp.Runtime.Leases.LeaseManager.GlobMatch("/workspace/myapp", "/workspace/myapp/**")
            .Should().BeTrue();
        // Siblings sharing the textual prefix MUST NOT match.
        Arcp.Runtime.Leases.LeaseManager.GlobMatch("/workspace/myapp-private/secret", "/workspace/myapp/**")
            .Should().BeFalse();
        Arcp.Runtime.Leases.LeaseManager.GlobMatch("/workspace/myapp.bak", "/workspace/myapp/**")
            .Should().BeFalse();
    }

    [Fact]
    public void AuthorizeOperation_double_star_does_not_leak_to_sibling_directories()
    {
        // End-to-end gate: fs.read:["/workspace/myapp/**"] authorizes files under the directory
        // but rejects a sibling like /workspace/myapp-private (spec §9.3).
        var manager = new Arcp.Runtime.Leases.LeaseManager();
        var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.FsRead] = new[] { "/workspace/myapp/**" },
        });

        var ok = () => manager.AuthorizeOperation(lease, null, LeaseNamespaces.FsRead, "/workspace/myapp/src/a.cs");
        var sibling = () => manager.AuthorizeOperation(lease, null, LeaseNamespaces.FsRead, "/workspace/myapp-private/secret");

        ok.Should().NotThrow();
        sibling.Should().Throw<Arcp.Core.Errors.PermissionDeniedException>();
    }
}
