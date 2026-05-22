// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;

namespace Arcp.Runtime.Credentials;

/// <summary>In-memory outstanding credential store for tests and single-process hosts.</summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<JobId, ConcurrentDictionary<string, byte>> _ids = new();

    public ValueTask AddAsync(JobId jobId, IReadOnlyList<string> credentialIds, CancellationToken cancellationToken)
    {
        var set = _ids.GetOrAdd(jobId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        foreach (var id in credentialIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            set.TryAdd(id, 0);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> ListAsync(JobId jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_ids.TryGetValue(jobId, out var set)) return ValueTask.FromResult<IReadOnlyList<string>>([]);
        return ValueTask.FromResult<IReadOnlyList<string>>(set.Keys.ToArray());
    }

    public ValueTask RemoveAsync(JobId jobId, string credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_ids.TryGetValue(jobId, out var set))
        {
            set.TryRemove(credentialId, out _);
            if (set.IsEmpty) _ids.TryRemove(jobId, out _);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyDictionary<JobId, IReadOnlyList<string>>> ListAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _ids.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Keys.ToArray());
        return ValueTask.FromResult<IReadOnlyDictionary<JobId, IReadOnlyList<string>>>(snapshot);
    }
}
