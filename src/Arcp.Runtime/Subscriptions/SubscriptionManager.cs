// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using Arcp.Core.Ids;

namespace Arcp.Runtime.Subscriptions;

/// <summary>Tracks per-job subscriber sessions so the runtime can fan <c>job.event</c> envelopes out
/// to every subscribed session (spec §7.6).</summary>
public sealed class SubscriptionManager
{
    private readonly Dictionary<JobId, HashSet<SessionId>> _byJob = new();
    private readonly object _gate = new();

    /// <summary>Subscribe.</summary>
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

    /// <summary>Unsubscribe.</summary>
    public void Unsubscribe(JobId jobId, SessionId sessionId)
    {
        lock (_gate)
        {
            if (_byJob.TryGetValue(jobId, out var set))
            {
                set.Remove(sessionId);
                if (set.Count == 0) _byJob.Remove(jobId);
            }
        }
    }

    /// <summary>Subscribers of.</summary>
    public IReadOnlyCollection<SessionId> SubscribersOf(JobId jobId)
    {
        lock (_gate)
        {
            return _byJob.TryGetValue(jobId, out var set) ? set.ToArray() : Array.Empty<SessionId>();
        }
    }

    /// <summary>Remove session.</summary>
    public void RemoveSession(SessionId sessionId)
    {
        lock (_gate)
        {
            // Snapshot keys because we may remove entries during iteration.
            var emptyJobs = new List<JobId>();
            foreach (var kv in _byJob)
            {
                kv.Value.Remove(sessionId);
                if (kv.Value.Count == 0) emptyJobs.Add(kv.Key);
            }
            foreach (var jobId in emptyJobs) _byJob.Remove(jobId);
        }
    }
}
