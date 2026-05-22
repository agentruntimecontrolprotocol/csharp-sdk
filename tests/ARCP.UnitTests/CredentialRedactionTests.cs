// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Arcp.Core.Messages;
using Arcp.Runtime.Credentials;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class CredentialRedactionTests
{
    [Fact]
    public void RedactFor_submitter_returns_unchanged()
    {
        var credentials = new[] { Credential("secret") };

        var redacted = CredentialRedaction.EmptyForNonSubmitter(credentials, isSubmitter: true);

        redacted.Should().BeSameAs(credentials);
    }

    [Fact]
    public void RedactFor_non_submitter_returns_empty_list()
    {
        var credentials = new[] { Credential("secret") };

        var redacted = CredentialRedaction.EmptyForNonSubmitter(credentials, isSubmitter: false);

        redacted.Should().BeEmpty();
    }

    [Fact]
    public void StripValues_removes_secret_but_preserves_metadata()
    {
        var credentials = new[] { Credential("secret") };

        var redacted = CredentialRedaction.StripValues(credentials);

        redacted.Should().ContainSingle();
        redacted[0].Id.Should().Be("cred_1");
        redacted[0].Value.Should().BeEmpty();
        redacted[0].Endpoint.Should().Be("https://example.invalid");
    }

    [Fact]
    public void JobListEntry_serialization_never_includes_credentials()
    {
        var entry = new JobListEntry
        {
            JobId = "job_1",
            Agent = "agent",
            Status = "running",
            CreatedAt = System.DateTimeOffset.UnixEpoch,
        };

        var json = JsonSerializer.Serialize(entry);

        json.Should().NotContain("credentials");
    }

    private static ProvisionedCredential Credential(string value) => new()
    {
        Id = "cred_1",
        Value = value,
        Endpoint = "https://example.invalid",
    };
}
