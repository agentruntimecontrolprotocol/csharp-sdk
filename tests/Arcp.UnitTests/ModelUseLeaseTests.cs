// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Runtime.Leases;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class ModelUseLeaseTests
{
    [Fact]
    public void ModelUse_subset_accepts_strict_subset()
    {
        var manager = new LeaseManager();
        var parent = LeaseWithModels("tier-fast/*");
        var child = LeaseWithModels("tier-fast/gpt-4o-mini");

        var act = () => manager.AssertSubset(parent, child);

        act.Should().NotThrow();
    }

    [Fact]
    public void ModelUse_subset_rejects_expansion()
    {
        var manager = new LeaseManager();
        var parent = LeaseWithModels("tier-fast/*");
        var child = LeaseWithModels("tier-premium/*");

        var act = () => manager.AssertSubset(parent, child);

        act.Should().Throw<LeaseSubsetViolationException>()
            .Which.Code.Should().Be(ErrorCode.LeaseSubsetViolation);
    }

    [Fact]
    public void AuthorizeModelUse_throws_permission_denied_on_miss()
    {
        var manager = new LeaseManager();
        var lease = LeaseWithModels("tier-fast/*");

        var act = () => manager.AuthorizeModelUse(lease, null, "tier-premium/gpt-4o");

        act.Should().Throw<PermissionDeniedException>()
            .Which.Code.Should().Be(ErrorCode.PermissionDenied);
    }

    [Fact]
    public void AuthorizeModelUse_passes_on_glob_match()
    {
        var manager = new LeaseManager();
        var lease = LeaseWithModels("tier-fast/*");

        var act = () => manager.AuthorizeModelUse(lease, null, "tier-fast/gpt-4o-mini");

        act.Should().NotThrow();
    }

    [Fact]
    public void ModelUse_empty_pattern_is_rejected()
    {
        var manager = new LeaseManager();
        var lease = LeaseWithModels("");

        var act = () => manager.Authorize(lease, null);

        act.Should().Throw<InvalidRequestException>();
    }

    private static Lease LeaseWithModels(params string[] models) =>
        new(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.ModelUse] = models,
        });
}
