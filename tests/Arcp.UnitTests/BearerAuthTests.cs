// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Auth;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class BearerAuthTests
{
    [Fact]
    public async Task StaticBearerVerifier_dictionary_overload_matches_known_token()
    {
        var p = new AuthPrincipal("alice");
        var v = new StaticBearerVerifier(new Dictionary<string, AuthPrincipal>
        {
            ["t-1"] = p,
        });
        (await v.VerifyAsync("t-1")).Should().Be(p);
        (await v.VerifyAsync("t-x")).Should().BeNull();
        (await v.VerifyAsync(null)).Should().BeNull();
        (await v.VerifyAsync("")).Should().BeNull();
    }

    [Fact]
    public async Task StaticBearerVerifier_params_overload_matches_known_token()
    {
        var p = new AuthPrincipal("bob");
        var v = new StaticBearerVerifier(("t-2", p), ("t-3", p));
        (await v.VerifyAsync("t-2")).Should().Be(p);
        (await v.VerifyAsync("t-3")).Should().Be(p);
        (await v.VerifyAsync("t-nope")).Should().BeNull();
    }

    [Fact]
    public async Task AllowAnyBearerVerifier_reflects_token_as_subject()
    {
        var v = new AllowAnyBearerVerifier();
        var p = await v.VerifyAsync("any-token");
        p.Should().NotBeNull();
        p!.Subject.Should().Be("any-token");
        (await v.VerifyAsync(null)).Should().BeNull();
        (await v.VerifyAsync("")).Should().BeNull();
    }
}
