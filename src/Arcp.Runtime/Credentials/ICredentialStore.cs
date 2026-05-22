// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;

namespace Arcp.Runtime.Credentials;

/// <summary>Stores outstanding provisioned credential ids until revocation succeeds.</summary>
public interface ICredentialStore
{
    ValueTask AddAsync(JobId jobId, IReadOnlyList<string> credentialIds, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<string>> ListAsync(JobId jobId, CancellationToken cancellationToken);

    ValueTask RemoveAsync(JobId jobId, string credentialId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<JobId, IReadOnlyList<string>>> ListAllAsync(CancellationToken cancellationToken);
}
