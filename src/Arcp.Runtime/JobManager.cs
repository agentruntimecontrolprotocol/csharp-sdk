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
public sealed partial class JobManager
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
        var watchdog = StartLeaseWatchdog(job, emit, cancellationToken);

        try
        {
            var result = await agent.RunAsync(ctx, job.CancellationToken).ConfigureAwait(false);
            await EmitSuccessResultAsync(job, result, emit, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Success);
        }
        catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
        {
            await EmitJobErrorAsync(job, emit, new JobErrorPayload
            {
                FinalStatus = "cancelled",
                Code = ErrorCode.Cancelled,
                Message = "Job cancelled",
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Cancelled);
        }
        catch (ArcpException ex)
        {
            await EmitJobErrorAsync(job, emit, new JobErrorPayload
            {
                FinalStatus = "error",
                Code = ex.Code,
                Message = ex.Message,
                Retryable = ex.Retryable,
                Detail = ex.Detail,
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Error);
        }
        catch (Exception ex)
        {
            await EmitJobErrorAsync(job, emit, new JobErrorPayload
            {
                FinalStatus = "error",
                Code = ErrorCode.InternalError,
                Message = ex.Message,
                Retryable = true,
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Error);
        }
        finally
        {
            await AwaitWatchdogAsync(watchdog).ConfigureAwait(false);
        }
    }

    private Task? StartLeaseWatchdog(Job job, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken)
    {
        // Spec §9.5.
        if (job.LeaseConstraints?.ExpiresAt is not { } expiresAt) return null;
        return RunLeaseWatchdog(job, expiresAt, emit, cancellationToken);
    }

    private static async Task AwaitWatchdogAsync(Task? watchdog)
    {
        if (watchdog is null) return;
        try { await watchdog.ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Watchdog may itself observe cancellation.
        }
    }

    private static async Task EmitSuccessResultAsync(Job job, object? result, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken)
    {
        var payload = job.StreamedResultId is { } rid
            ? new JobResultPayload
            {
                FinalStatus = "success",
                ResultId = rid.Value,
                ResultSize = job.StreamedResultSize,
                Summary = result as string,
            }
            : BuildInlineResultPayload(job, result);

        await emit(BuildEnvelope(job, MessageTypeNames.JobResult, payload), cancellationToken).ConfigureAwait(false);
    }

    private static JobResultPayload BuildInlineResultPayload(Job job, object? result)
    {
        job.MarkInlineResult();
        return new JobResultPayload
        {
            FinalStatus = "success",
            Result = result is null ? null : ArcpJson.ToJsonElement(result),
        };
    }

    private static ValueTask EmitJobErrorAsync(Job job, Func<Envelope, CancellationToken, ValueTask> emit, JobErrorPayload payload, CancellationToken cancellationToken) =>
        emit(BuildEnvelope(job, MessageTypeNames.JobError, payload), cancellationToken);

    private static Envelope BuildEnvelope(Job job, string type, object payload) => new()
    {
        Type = type,
        SessionId = job.SessionId.Value,
        JobId = job.JobId.Value,
        TraceId = job.TraceId?.Value,
        Payload = payload,
    };

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

}
