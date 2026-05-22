// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Agents;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Wire;
using Arcp.Runtime;
using Arcp.Runtime.Credentials;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arcp.UnitTests;

public class CredentialBudgetMappingTests
{
    [Fact]
    public async Task Provisioner_throwing_budget_exhausted_surfaces_code()
    {
        var manager = Manager(new ThrowingProvisioner(new BudgetExhaustedException("spent")));
        var job = TestJob();

        var act = () => manager.IssueForJobAsync(job, CancellationToken.None).AsTask();

        var ex = await act.Should().ThrowAsync<BudgetExhaustedException>();
        ex.Which.Code.Should().Be(ErrorCode.BudgetExhausted);
    }

    [Fact]
    public async Task Provisioner_throwing_402_maps_to_budget_exhausted()
    {
        var upstream = new HttpRequestException("payment required", null, HttpStatusCode.PaymentRequired);
        var manager = Manager(new ThrowingProvisioner(upstream));
        var job = TestJob();

        var act = () => manager.IssueForJobAsync(job, CancellationToken.None).AsTask();

        var ex = await act.Should().ThrowAsync<BudgetExhaustedException>();
        ex.Which.Code.Should().Be(ErrorCode.BudgetExhausted);
    }

    private static CredentialManager Manager(ICredentialProvisioner provisioner) =>
        new(provisioner, new InMemoryCredentialStore(), NullLogger.Instance, TimeProvider.System);

    private static Job TestJob() => new(
        JobId.New(),
        SessionId.New(),
        new AgentRef("agent"),
        new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        }),
        null,
        JsonSerializer.SerializeToElement(new { }),
        null,
        TraceId.New(),
        null,
        "submitter",
        System.DateTimeOffset.UtcNow,
        (_, _) => ValueTask.CompletedTask,
        TimeProvider.System,
        CancellationToken.None);

    private sealed class ThrowingProvisioner(System.Exception exception) : ICredentialProvisioner
    {
        public ValueTask<IReadOnlyList<ProvisionedCredential>> IssueAsync(
            Lease lease,
            LeaseConstraints? constraints,
            CredentialIssueContext context,
            CancellationToken cancellationToken) =>
            throw exception;

        public ValueTask RevokeAsync(string credentialId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
