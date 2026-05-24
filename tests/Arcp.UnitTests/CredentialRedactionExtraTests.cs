// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Reflection;
using Arcp.Core.Messages;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class CredentialRedactionExtraTests
{
    // CredentialRedaction is `internal`. Reach it by reflection so test coverage doesn't
    // require widening visibility just for tests.
    private static readonly System.Type Redaction =
        typeof(Arcp.Runtime.ArcpServer).Assembly.GetType("Arcp.Runtime.Credentials.CredentialRedaction")!;

    [Fact]
    public void EmptyForNonSubmitter_returns_input_for_owner_and_empty_for_others()
    {
        var creds = new[] { new ProvisionedCredential { Id = "c1", Scheme = "bearer", Value = "secret", Endpoint = "https://example/" } };
        var emptyForOwner = (IReadOnlyList<ProvisionedCredential>)Redaction
            .GetMethod("EmptyForNonSubmitter", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { creds, true })!;
        var emptyForOther = (IReadOnlyList<ProvisionedCredential>)Redaction
            .GetMethod("EmptyForNonSubmitter", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { creds, false })!;
        emptyForOwner.Should().HaveCount(1);
        emptyForOther.Should().BeEmpty();
    }

    [Fact]
    public void StripValues_removes_value_field_but_keeps_metadata()
    {
        var creds = new[] { new ProvisionedCredential { Id = "c1", Scheme = "bearer", Value = "supersecret", Endpoint = "https://example/" } };
        var stripped = (IReadOnlyList<ProvisionedCredential>)Redaction
            .GetMethod("StripValues", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { creds })!;
        stripped.Should().HaveCount(1);
        stripped[0].Id.Should().Be("c1");
        stripped[0].Scheme.Should().Be("bearer");
        stripped[0].Value.Should().BeEmpty();
    }
}
