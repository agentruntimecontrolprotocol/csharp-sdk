// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Agents;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Wire;
using Arcp.Runtime.Agents;
using Arcp.Runtime.Authorization;
using Arcp.Runtime.Leases;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

/// <summary>Runtime-wide registry of running jobs. Coordinates submit → accept → terminal, idempotency
/// dedup, cancellation, lease watchdog, and subscription fan-out.</summary>
public sealed class JobManager
{
    private readonly ConcurrentDictionary<JobId, Job> _jobs = new();
    private readonly ConcurrentDictionary<string, JobId> _idempotency = new(StringComparer.Ordinal);
    private readonly AgentRegistry _agents;
    private readonly LeaseManager _leases;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;

    public JobManager(AgentRegistry agents, LeaseManager leases, TimeProvider time, ILoggerFactory loggers)
    {
        _agents = agents;
        _leases = leases;
        _time = time;
        _loggers = loggers;
    }

    public IEnumerable<Job> Jobs => _jobs.Values;

    public bool TryGet(JobId id, out Job? job)
    {
        var ok = _jobs.TryGetValue(id, out var j);
        job = j;
        return ok;
    }

    /// <summary>Submit a job. The caller (SessionState) hands in the envelope; this method returns
    /// the <see cref="Job"/> to run asynchronously plus the <c>job.accepted</c> payload.</summary>
    public Job Submit(JobSubmitPayload submit, SessionId sessionId, string? submitterPrincipal,
                       Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken parentCancellation,
                       out JobAcceptedPayload accepted)
    {
        // Idempotency check (spec §7.2).
        if (!string.IsNullOrEmpty(submit.IdempotencyKey))
        {
            var key = $"{submitterPrincipal ?? "*"}|{submit.IdempotencyKey}";
            if (_idempotency.TryGetValue(key, out var existingId) && _jobs.TryGetValue(existingId, out var existing))
            {
                accepted = BuildAccepted(existing);
                return existing;
            }
        }

        if (!AgentRef.TryParse(submit.Agent, null, out var requested))
            throw new InvalidRequestException($"Malformed agent '{submit.Agent}'");
        var (resolved, _) = _agents.Resolve(requested);

        var lease = _leases.Authorize(submit.LeaseRequest, submit.LeaseConstraints);

        var jobId = JobId.New();
        var traceId = TraceId.New();
        var job = new Job(
            jobId, sessionId, resolved, lease, submit.LeaseConstraints,
            submit.Input, submit.IdempotencyKey, traceId, submit.ParentJobId, submitterPrincipal,
            _time.GetUtcNow(), emit, _time, parentCancellation);
        _jobs[jobId] = job;
        if (!string.IsNullOrEmpty(submit.IdempotencyKey))
        {
            _idempotency.TryAdd($"{submitterPrincipal ?? "*"}|{submit.IdempotencyKey}", jobId);
        }

        accepted = BuildAccepted(job);
        return job;
    }

    private static JobAcceptedPayload BuildAccepted(Job job) => new()
    {
        JobId = job.JobId.Value,
        Agent = job.Agent.ToString(),
        Lease = job.Lease,
        LeaseConstraints = job.LeaseConstraints,
        Budget = job.BudgetLedger.IsActive ? job.BudgetLedger.Initial : null,
        AcceptedAt = job.CreatedAt,
        TraceId = job.TraceId?.Value,
        ParentJobId = job.ParentJobId,
    };

    public async Task RunAsync(Job job, IAgent agent, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken)
    {
        job.MarkRunning();
        var ctx = new JobContext(job, _loggers.CreateLogger($"Arcp.Job.{job.JobId.Value}"));

        // Start lease watchdog if constrained (spec §9.5).
        Task? watchdog = null;
        if (job.LeaseConstraints?.ExpiresAt is { } expiresAt)
        {
            watchdog = RunLeaseWatchdog(job, expiresAt, emit, cancellationToken);
        }

        try
        {
            var result = await agent.RunAsync(ctx, job.CancellationToken).ConfigureAwait(false);

            if (job.StreamedResultId is { } rid)
            {
                var payload = new JobResultPayload
                {
                    FinalStatus = "success",
                    ResultId = rid.Value,
                    ResultSize = job.StreamedResultSize,
                    Summary = result as string,
                };
                await emit(new Envelope
                {
                    Type = MessageTypeNames.JobResult,
                    SessionId = job.SessionId.Value,
                    JobId = job.JobId.Value,
                    TraceId = job.TraceId?.Value,
                    Payload = payload,
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                job.MarkInlineResult();
                var payload = new JobResultPayload
                {
                    FinalStatus = "success",
                    Result = result is null ? null : ArcpJson.ToJsonElement(result),
                };
                await emit(new Envelope
                {
                    Type = MessageTypeNames.JobResult,
                    SessionId = job.SessionId.Value,
                    JobId = job.JobId.Value,
                    TraceId = job.TraceId?.Value,
                    Payload = payload,
                }, cancellationToken).ConfigureAwait(false);
            }
            job.MarkTerminal(JobStatus.Success);
        }
        catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
        {
            var payload = new JobErrorPayload
            {
                FinalStatus = "cancelled",
                Code = ErrorCode.Cancelled,
                Message = "Job cancelled",
            };
            await emit(new Envelope
            {
                Type = MessageTypeNames.JobError,
                SessionId = job.SessionId.Value,
                JobId = job.JobId.Value,
                TraceId = job.TraceId?.Value,
                Payload = payload,
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Cancelled);
        }
        catch (ArcpException ex)
        {
            var payload = new JobErrorPayload
            {
                FinalStatus = "error",
                Code = ex.Code,
                Message = ex.Message,
                Retryable = ex.Retryable,
                Detail = ex.Detail,
            };
            await emit(new Envelope
            {
                Type = MessageTypeNames.JobError,
                SessionId = job.SessionId.Value,
                JobId = job.JobId.Value,
                TraceId = job.TraceId?.Value,
                Payload = payload,
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Error);
        }
        catch (Exception ex)
        {
            var payload = new JobErrorPayload
            {
                FinalStatus = "error",
                Code = ErrorCode.InternalError,
                Message = ex.Message,
                Retryable = true,
            };
            await emit(new Envelope
            {
                Type = MessageTypeNames.JobError,
                SessionId = job.SessionId.Value,
                JobId = job.JobId.Value,
                TraceId = job.TraceId?.Value,
                Payload = payload,
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Error);
        }
        finally
        {
            if (watchdog is not null)
            {
                try { await watchdog.ConfigureAwait(false); } catch { /* watchdog may itself observe cancellation */ }
            }
        }
    }

    private async Task RunLeaseWatchdog(Job job, DateTimeOffset expiresAt, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow();
            var delay = expiresAt - now;
            while (delay > TimeSpan.Zero && !job.CancellationToken.IsCancellationRequested)
            {
                var step = delay > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
                await Task.Delay(step, _time, cancellationToken).ConfigureAwait(false);
                now = _time.GetUtcNow();
                delay = expiresAt - now;
            }
            if (!job.CancellationToken.IsCancellationRequested)
            {
                // Emit a tool_result.error then job.error per spec §13.4.
                await job.EmitEventAsync(EventKinds.ToolResult, new ToolResultBody
                {
                    CallId = $"lease_{job.JobId.Value}",
                    Error = new ToolError
                    {
                        Code = ErrorCode.LeaseExpired,
                        Message = $"Lease expired at {expiresAt:O}",
                        Retryable = false,
                    },
                }, cancellationToken).ConfigureAwait(false);
                job.CancellationSource.Cancel();
            }
        }
        catch (OperationCanceledException) { /* watchdog cancelled because job finished first */ }
    }

    public bool Cancel(JobId jobId, string? requesterPrincipal, string? reason)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return false;
        // Spec §7.6: subscription does NOT grant cancel authority; only submitter may cancel.
        if (requesterPrincipal is not null && job.SubmitterPrincipal is not null &&
            !string.Equals(requesterPrincipal, job.SubmitterPrincipal, StringComparison.Ordinal))
        {
            throw new PermissionDeniedException("Subscribers MUST NOT cancel jobs (spec §7.6)");
        }
        job.CancellationSource.Cancel();
        return true;
    }

    public IReadOnlyList<JobListEntry> List(string? requesterPrincipal, IJobAuthorizationPolicy policy,
                                            JobListFilter? filter, int? limit, string? cursor, out string? nextCursor)
    {
        var jobs = _jobs.Values
            .Where(j => string.IsNullOrEmpty(requesterPrincipal) ||
                        string.Equals(j.SubmitterPrincipal, requesterPrincipal, StringComparison.Ordinal) ||
                        policy.CanObserve(j.SubmitterPrincipal, new Core.Auth.AuthPrincipal(requesterPrincipal)))
            .ToList();

        if (filter is not null)
        {
            if (filter.Status is { Count: > 0 } statuses)
                jobs = jobs.Where(j => statuses.Contains(MapStatus(j.Status), StringComparer.Ordinal)).ToList();
            if (!string.IsNullOrEmpty(filter.Agent))
            {
                var a = filter.Agent;
                jobs = jobs.Where(j => j.Agent.Name == a || j.Agent.ToString() == a).ToList();
            }
            if (filter.CreatedAfter is { } after)
                jobs = jobs.Where(j => j.CreatedAt > after).ToList();
        }

        jobs = jobs.OrderBy(j => j.CreatedAt).ToList();
        var skip = ParseCursor(cursor);
        var take = limit ?? 100;
        var page = jobs.Skip(skip).Take(take).ToList();
        nextCursor = skip + page.Count < jobs.Count ? EncodeCursor(skip + page.Count) : null;

        return page.Select(j => new JobListEntry
        {
            JobId = j.JobId.Value,
            Agent = j.Agent.ToString(),
            Status = MapStatus(j.Status),
            Lease = j.Lease,
            ParentJobId = j.ParentJobId,
            CreatedAt = j.CreatedAt,
            TraceId = j.TraceId?.Value,
        }).ToArray();
    }

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
        try { return int.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
