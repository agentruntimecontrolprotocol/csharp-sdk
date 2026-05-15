// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Arcp.Core.Ids;

namespace Arcp.Runtime.Subscriptions;

/// <summary>Tracks per-job subscriber sessions so the runtime can fan <c>job.event</c> envelopes out
/// to every subscribed session (spec §7.6).</summary>
public sealed class SubscriptionManager
{
    private readonly ConcurrentDictionary<JobId, HashSet<SessionId>> _byJob = new();
    private readonly object _gate = new();

    public void Subscribe(JobId jobId, SessionId sessionId)
    {
        lock (_gate)
        {
            if (!_byJob.TryGetValue(jobId, out var set))
            {
                set = new HashSet<SessionId>();
                _byJob[jobId] = set;
            }
            set.Add(sessionId);
        }
    }

    public void Unsubscribe(JobId jobId, SessionId sessionId)
    {
        lock (_gate)
        {
            if (_byJob.TryGetValue(jobId, out var set))
            {
                set.Remove(sessionId);
                if (set.Count == 0) _byJob.TryRemove(jobId, out _);
            }
        }
    }

    public IReadOnlyCollection<SessionId> SubscribersOf(JobId jobId)
    {
        lock (_gate)
        {
            return _byJob.TryGetValue(jobId, out var set) ? set.ToArray() : Array.Empty<SessionId>();
        }
    }

    public void RemoveSession(SessionId sessionId)
    {
        lock (_gate)
        {
            foreach (var kv in _byJob)
            {
                kv.Value.Remove(sessionId);
            }
        }
    }
}
