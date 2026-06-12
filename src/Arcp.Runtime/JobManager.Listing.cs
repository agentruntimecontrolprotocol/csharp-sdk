// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Arcp.Core.Auth;
using Arcp.Core.Messages;
using Arcp.Runtime.Authorization;

namespace Arcp.Runtime;

public sealed partial class JobManager
{
    /// <summary>List jobs visible to <paramref name="requesterPrincipal"/>, paged by a stable keyset
    /// cursor on <c>(created_at, job_id)</c> (spec §6.6). Page materialization is bounded to
    /// <c>limit + 1</c> entries regardless of how many jobs are visible, and ordering is stable when
    /// multiple jobs share the same <c>CreatedAt</c>.</summary>
    public IReadOnlyList<JobListEntry> List(string? requesterPrincipal, IJobAuthorizationPolicy policy,
        JobListFilter? filter, int? limit, string? cursor, out string? nextCursor)
    {
        var take = limit is > 0 ? limit.Value : 100;
        var after = DecodeCursor(cursor);

        // Convert the status filter to a set once per request rather than per job.
        var statusSet = filter?.Status is { Count: > 0 } statuses
            ? new HashSet<string>(statuses, StringComparer.Ordinal)
            : null;

        // Single streaming pass: keep only the smallest take+1 entries strictly after the cursor.
        // This bounds page materialization to take+1 entries instead of sorting/rematerializing the
        // full visible job set on every page (spec §6.6).
        var page = new List<Job>(take + 1);
        foreach (var job in FilterByPrincipal(requesterPrincipal, policy))
        {
            if (!MatchesFilter(job, filter, statusSet)) continue;
            var key = JobKey.From(job);
            if (after is { } a && JobKey.Compare(key, a) <= 0) continue; // at or before the cursor
            InsertBounded(page, job, take + 1);
        }

        // A take+1th entry means there is at least one more page; the cursor is the last returned key.
        nextCursor = page.Count > take ? EncodeCursor(JobKey.From(page[take - 1])) : null;
        var count = Math.Min(page.Count, take);
        var result = new JobListEntry[count];
        for (var i = 0; i < count; i++) result[i] = ToListEntry(page[i]);
        return result;
    }

    /// <summary>Jobs the requester is allowed to see. Fail closed: an empty/absent principal is NOT a
    /// wildcard — it sees only what the authorization policy explicitly permits, never the full
    /// cross-principal set (spec §6.6, §14).</summary>
    private IEnumerable<Job> FilterByPrincipal(string? requesterPrincipal, IJobAuthorizationPolicy policy)
    {
        if (string.IsNullOrEmpty(requesterPrincipal))
        {
            var anonymous = new AuthPrincipal(string.Empty);
            return _jobs.Values.Where(j => policy.CanObserve(j.SubmitterPrincipal, anonymous));
        }

        var principal = new AuthPrincipal(requesterPrincipal);
        return _jobs.Values.Where(j =>
            string.Equals(j.SubmitterPrincipal, requesterPrincipal, StringComparison.Ordinal) ||
            policy.CanObserve(j.SubmitterPrincipal, principal));
    }

    private static bool MatchesFilter(Job j, JobListFilter? filter, HashSet<string>? statusSet)
    {
        if (filter is null) return true;
        if (statusSet is not null && !statusSet.Contains(MapStatus(j.Status))) return false;
        if (!string.IsNullOrEmpty(filter.Agent) && j.Agent.Name != filter.Agent && j.Agent.ToString() != filter.Agent)
            return false;
        if (filter.CreatedAfter is { } after && j.CreatedAt <= after) return false;
        return true;
    }

    /// <summary>Insert <paramref name="job"/> into the ascending-ordered <paramref name="page"/>,
    /// keeping at most <paramref name="cap"/> entries. Jobs ordering larger than everything retained
    /// are discarded without growing the list, so the buffer never exceeds <c>cap</c>.</summary>
    private static void InsertBounded(List<Job> page, Job job, int cap)
    {
        var key = JobKey.From(job);
        int lo = 0, hi = page.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (JobKey.Compare(JobKey.From(page[mid]), key) < 0) lo = mid + 1;
            else hi = mid;
        }
        if (lo >= cap) return; // larger than every retained entry; cannot be in the smallest `cap`
        page.Insert(lo, job);
        if (page.Count > cap) page.RemoveAt(page.Count - 1);
    }

    private readonly record struct JobKey(DateTimeOffset CreatedAt, string JobId)
    {
        public static JobKey From(Job j) => new(j.CreatedAt, j.JobId.Value);

        public static int Compare(JobKey a, JobKey b)
        {
            var byTime = a.CreatedAt.UtcDateTime.CompareTo(b.CreatedAt.UtcDateTime);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.JobId, b.JobId);
        }
    }

    private static JobListEntry ToListEntry(Job j) => new()
    {
        // Spec §14: list_jobs is an introspection surface and never carries credential secrets.
        JobId = j.JobId.Value,
        Agent = j.Agent.ToString(),
        Status = MapStatus(j.Status),
        Lease = j.Lease,
        ParentJobId = j.ParentJobId,
        CreatedAt = j.CreatedAt,
        TraceId = j.TraceId?.Value,
        LastEventSeq = j.LastEmittedSeq,
    };

    private static string MapStatus(JobStatus s) => s switch
    {
        JobStatus.Pending => "pending",
        JobStatus.Running => "running",
        JobStatus.Success => "success",
        JobStatus.Error => "error",
        JobStatus.Cancelled => "cancelled",
        JobStatus.TimedOut => "timed_out",
        _ => "unknown",
    };

    private static JobKey? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var sep = raw.IndexOf('|');
            if (sep < 0) return null;
            var ticks = long.Parse(raw[..sep], CultureInfo.InvariantCulture);
            var jobId = raw[(sep + 1)..];
            return new JobKey(new DateTimeOffset(ticks, TimeSpan.Zero), jobId);
        }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }

    private static string EncodeCursor(JobKey key)
    {
        var raw = $"{key.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{key.JobId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }
}
