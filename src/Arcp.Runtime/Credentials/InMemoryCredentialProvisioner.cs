// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Leases;
using Arcp.Core.Messages;

namespace Arcp.Runtime.Credentials;

/// <summary>Deterministic in-memory credential provisioner for tests and samples.</summary>
public sealed class InMemoryCredentialProvisioner : ICredentialProvisioner
{
    private readonly ConcurrentBag<string> _revoked = [];

    /// <summary>Gets the revoked ids.</summary>
    public IReadOnlyCollection<string> RevokedIds => _revoked.ToArray();

    /// <summary>Issue (asynchronous).</summary>
    public ValueTask<IReadOnlyList<ProvisionedCredential>> IssueAsync(
        Lease lease,
        LeaseConstraints? constraints,
        CredentialIssueContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var credential = new ProvisionedCredential
        {
            Id = $"cred_{context.JobId.Value}",
            Value = $"test-token-{context.JobId.Value}",
            Endpoint = "https://credentials.example.invalid",
            Profile = "test",
            Constraints = new CredentialConstraints
            {
                CostBudget = lease.Get(LeaseNamespaces.CostBudget).ToArray(),
                ModelUse = lease.Get(LeaseNamespaces.ModelUse).ToArray(),
                ExpiresAt = constraints?.ExpiresAt,
            },
        };
        return ValueTask.FromResult<IReadOnlyList<ProvisionedCredential>>([credential]);
    }

    /// <summary>Revoke (asynchronous).</summary>
    public ValueTask RevokeAsync(string credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _revoked.Add(credentialId);
        return ValueTask.CompletedTask;
    }
}
