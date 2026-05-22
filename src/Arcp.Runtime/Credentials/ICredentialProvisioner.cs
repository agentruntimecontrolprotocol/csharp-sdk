// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Leases;
using Arcp.Core.Messages;

namespace Arcp.Runtime.Credentials;

/// <summary>Issues and revokes lease-bound credentials for accepted jobs.</summary>
public interface ICredentialProvisioner
{
    /// <summary>Issue credentials sized for this job's lease.</summary>
    ValueTask<IReadOnlyList<ProvisionedCredential>> IssueAsync(
        Lease lease,
        LeaseConstraints? constraints,
        CredentialIssueContext context,
        CancellationToken cancellationToken);

    /// <summary>Best-effort revoke for a provisioned credential id.</summary>
    ValueTask RevokeAsync(string credentialId, CancellationToken cancellationToken);
}
