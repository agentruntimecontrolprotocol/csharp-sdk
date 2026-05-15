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
    public IReadOnlyList<JobListEntry> List(string? requesterPrincipal, IJobAuthorizationPolicy policy,
        JobListFilter? filter, int? limit, string? cursor, out string? nextCursor)
    {
        var jobs = FilterByPrincipal(requesterPrincipal, policy);
        jobs = ApplyFilter(jobs, filter);
        jobs = jobs.OrderBy(j => j.CreatedAt).ToList();

        var skip = ParseCursor(cursor);
        var take = limit ?? 100;
        var page = jobs.Skip(skip).Take(take).ToList();
        nextCursor = skip + page.Count < jobs.Count ? EncodeCursor(skip + page.Count) : null;

        return page.Select(ToListEntry).ToArray();
    }

    private List<Job> FilterByPrincipal(string? requesterPrincipal, IJobAuthorizationPolicy policy) =>
        _jobs.Values
            .Where(j => string.IsNullOrEmpty(requesterPrincipal) ||
                        string.Equals(j.SubmitterPrincipal, requesterPrincipal, StringComparison.Ordinal) ||
                        policy.CanObserve(j.SubmitterPrincipal, new AuthPrincipal(requesterPrincipal)))
            .ToList();

    private static List<Job> ApplyFilter(List<Job> jobs, JobListFilter? filter)
    {
        if (filter is null) return jobs;
        if (filter.Status is { Count: > 0 } statuses)
            jobs = jobs.Where(j => statuses.Contains(MapStatus(j.Status), StringComparer.Ordinal)).ToList();
        if (!string.IsNullOrEmpty(filter.Agent))
        {
            var a = filter.Agent;
            jobs = jobs.Where(j => j.Agent.Name == a || j.Agent.ToString() == a).ToList();
        }
        if (filter.CreatedAfter is { } after)
            jobs = jobs.Where(j => j.CreatedAt > after).ToList();
        return jobs;
    }

    private static JobListEntry ToListEntry(Job j) => new()
    {
        JobId = j.JobId.Value,
        Agent = j.Agent.ToString(),
        Status = MapStatus(j.Status),
        Lease = j.Lease,
        ParentJobId = j.ParentJobId,
        CreatedAt = j.CreatedAt,
        TraceId = j.TraceId?.Value,
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

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        try { return int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), CultureInfo.InvariantCulture); }
        catch (FormatException) { return 0; }
    }

    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));
}
