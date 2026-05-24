// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;

namespace Arcp.Runtime.Credentials;

/// <summary>Stores outstanding provisioned credential ids until revocation succeeds.</summary>
public interface ICredentialStore
{
    /// <summary>Add (asynchronous).</summary>
    ValueTask AddAsync(JobId jobId, IReadOnlyList<string> credentialIds, CancellationToken cancellationToken);

    /// <summary>List (asynchronous).</summary>
    ValueTask<IReadOnlyList<string>> ListAsync(JobId jobId, CancellationToken cancellationToken);

    /// <summary>Remove (asynchronous).</summary>
    ValueTask RemoveAsync(JobId jobId, string credentialId, CancellationToken cancellationToken);

    /// <summary>List all (asynchronous).</summary>
    ValueTask<IReadOnlyDictionary<JobId, IReadOnlyList<string>>> ListAllAsync(CancellationToken cancellationToken);
}
