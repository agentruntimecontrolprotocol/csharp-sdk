// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
using Arcp.Runtime.Budget;
using Arcp.Runtime.Credentials;
using Arcp.Runtime.Leases;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

/// <summary>Runtime-wide registry of running jobs. Coordinates submit → accept → terminal, idempotency
/// dedup, cancellation, lease watchdog, and subscription fan-out.</summary>
public sealed partial class JobManager
{
    private readonly ConcurrentDictionary<JobId, Job> _jobs = new();
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _idempotency = new(StringComparer.Ordinal);
    private readonly AgentRegistry _agents;
    private readonly LeaseManager _leases;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly CredentialManager? _credentials;
    private readonly int _idempotencyWindowSec;
    private readonly bool _fatalBudgetExhaustion;

    /// <summary>Stored record for an idempotency key: original submission fingerprint plus issue time.</summary>
    private sealed record IdempotencyRecord(JobId JobId, string Fingerprint, DateTimeOffset CreatedAt);

    /// <summary>Initializes a new <see cref="JobManager"/>.</summary>
    public JobManager(
        AgentRegistry agents,
        LeaseManager leases,
        TimeProvider time,
        ILoggerFactory loggers,
        CredentialManager? credentials = null,
        int idempotencyWindowSec = 3600,
        bool fatalBudgetExhaustion = false)
    {
        _agents = agents;
        _leases = leases;
        _time = time;
        _loggers = loggers;
        _credentials = credentials;
        _idempotencyWindowSec = idempotencyWindowSec > 0 ? idempotencyWindowSec : 3600;
        _fatalBudgetExhaustion = fatalBudgetExhaustion;
    }

    /// <summary>Initializes a new <see cref="JobManager"/> without credential provisioning.</summary>
    public JobManager(AgentRegistry agents, LeaseManager leases, TimeProvider time, ILoggerFactory loggers)
        : this(agents, leases, time, loggers, null)
    {
    }

    /// <summary>All jobs currently tracked, in arbitrary order.</summary>
    public IEnumerable<Job> Jobs => _jobs.Values;

    /// <summary>Look up a job by id.</summary>
    public bool TryGet(JobId id, out Job? job)
    {
        var ok = _jobs.TryGetValue(id, out var j);
        job = j;
        return ok;
    }

    /// <summary>Submit a job. The caller (SessionState) hands in the envelope; this method returns
    /// the <see cref="Job"/> to run asynchronously plus the <c>job.accepted</c> payload.
    /// <paramref name="inboundTraceId"/> propagates the envelope's <c>trace_id</c> per spec §11.</summary>
    public async Task<(Job Job, JobAcceptedPayload Accepted)> SubmitAsync(
        JobSubmitPayload submit,
        SessionId sessionId,
        string? submitterPrincipal,
        Func<Envelope, CancellationToken, ValueTask> emit,
        TraceId? inboundTraceId,
        CancellationToken parentCancellation,
        CancellationToken cancellationToken = default)
    {
        // Idempotency check (spec §7.2). Fingerprint guards against replay with mismatched payload.
        string? idemKey = null;
        string? fingerprint = null;
        if (!string.IsNullOrEmpty(submit.IdempotencyKey))
        {
            idemKey = $"{submitterPrincipal ?? "*"}|{submit.IdempotencyKey}";
            fingerprint = ComputeFingerprint(submit);
            if (_idempotency.TryGetValue(idemKey, out var existingRecord))
            {
                var age = _time.GetUtcNow() - existingRecord.CreatedAt;
                if (age.TotalSeconds <= _idempotencyWindowSec)
                {
                    if (!string.Equals(existingRecord.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        throw new DuplicateKeyException(
                            $"Idempotency key '{submit.IdempotencyKey}' was previously used with a different payload");
                    }
                    if (_jobs.TryGetValue(existingRecord.JobId, out var existing))
                    {
                        return (existing, BuildAccepted(existing));
                    }
                }
                else
                {
                    _idempotency.TryRemove(idemKey, out _);
                }
            }
        }

        if (!AgentRef.TryParse(submit.Agent, null, out var requested))
            throw new InvalidRequestException($"Malformed agent '{submit.Agent}'");
        var (resolved, _) = _agents.Resolve(requested);

        var lease = _leases.Authorize(submit.LeaseRequest, submit.LeaseConstraints);
        if (submit.ParentJobId is not null)
        {
            AssertChildLeaseIsSubset(submit.ParentJobId, lease, submit.LeaseConstraints);
        }

        var jobId = JobId.New();
        // Spec §11: propagate inbound W3C trace context when present; otherwise mint a fresh one.
        var traceId = inboundTraceId ?? TraceId.New();
        var job = new Job(
            jobId, sessionId, resolved, lease, submit.LeaseConstraints,
            submit.Input, submit.IdempotencyKey, traceId, submit.ParentJobId, submitterPrincipal,
            submit.MaxRuntimeSec,
            _time.GetUtcNow(), emit, _time, parentCancellation);
        if (_credentials is not null)
        {
            await _credentials.IssueForJobAsync(job, cancellationToken).ConfigureAwait(false);
        }

        _jobs[jobId] = job;
        if (idemKey is not null && fingerprint is not null)
        {
            _idempotency[idemKey] = new IdempotencyRecord(jobId, fingerprint, _time.GetUtcNow());
        }

        return (job, BuildAccepted(job));
    }

    private void AssertChildLeaseIsSubset(string parentJobId, Lease child, LeaseConstraints? childConstraints)
    {
        if (!JobId.TryParse(parentJobId, null, out var parsed))
            throw new InvalidRequestException("parent_job_id is not a valid job_id");
        if (!_jobs.TryGetValue(parsed, out var parent))
            throw new JobNotFoundException($"parent job {parentJobId} not found");

        _leases.AssertSubset(parent.Lease, child, parent.BudgetLedger.Remaining, parent.LeaseConstraints, childConstraints);
    }

    private static JobAcceptedPayload BuildAccepted(Job job)
    {
        var credentials = job.Credentials;
        return new JobAcceptedPayload
        {
            JobId = job.JobId.Value,
            Agent = job.Agent.ToString(),
            Lease = job.Lease,
            LeaseConstraints = job.LeaseConstraints,
            Budget = job.BudgetLedger.IsActive ? job.BudgetLedger.Initial : null,
            AcceptedAt = job.CreatedAt,
            TraceId = job.TraceId?.Value,
            ParentJobId = job.ParentJobId,
            Credentials = credentials.Count == 0 ? null : credentials,
        };
    }

    /// <summary>Run a job. Owns the agent invocation, lease watchdog, runtime watchdog, and terminal emission.</summary>
    public async Task RunAsync(Job job, IAgent agent, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken)
    {
        job.MarkRunning();
        var ctx = new JobContext(job, _loggers.CreateLogger($"Arcp.Job.{job.JobId.Value}"), _credentials, _fatalBudgetExhaustion, _leases);

        // Watchdog cancellation source — cancelled in `finally` so the watchdog never outlives
        // the job and never emits a late lease-expired event after the terminal result.
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watchdog = StartLeaseWatchdog(job, emit, watchdogCts.Token);
        var runtimeWatchdog = StartRuntimeWatchdog(job, watchdogCts.Token);

        try
        {
            var result = await agent.RunAsync(ctx, job.CancellationToken).ConfigureAwait(false);
            // If a runtime-limit triggered cancellation, surface as TIMEOUT rather than CANCELLED.
            if (job.RuntimeLimitExceeded)
            {
                await EmitTimeoutAsync(job, emit, cancellationToken).ConfigureAwait(false);
                job.MarkTerminal(JobStatus.TimedOut);
            }
            else
            {
                await EmitSuccessResultAsync(job, result, emit, cancellationToken).ConfigureAwait(false);
                job.MarkTerminal(JobStatus.Success);
            }
        }
        catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
        {
            if (job.RuntimeLimitExceeded)
            {
                await EmitTimeoutAsync(job, emit, cancellationToken).ConfigureAwait(false);
                job.MarkTerminal(JobStatus.TimedOut);
            }
            else if (job.LeaseExpired)
            {
                await EmitJobErrorAsync(job, emit, new JobErrorPayload
                {
                    FinalStatus = "error",
                    Code = ErrorCode.LeaseExpired,
                    Message = "Lease expired",
                    Retryable = false,
                }, cancellationToken).ConfigureAwait(false);
                job.MarkTerminal(JobStatus.Error);
            }
            else
            {
                await EmitJobErrorAsync(job, emit, new JobErrorPayload
                {
                    FinalStatus = "cancelled",
                    Code = ErrorCode.Cancelled,
                    Message = "Job cancelled",
                }, cancellationToken).ConfigureAwait(false);
                job.MarkTerminal(JobStatus.Cancelled);
            }
        }
        catch (BudgetExhaustedException ex)
        {
            await EmitJobErrorAsync(job, emit, new JobErrorPayload
            {
                FinalStatus = "error",
                Code = ErrorCode.BudgetExhausted,
                Message = ex.Message,
                Retryable = false,
                Detail = ex.Detail,
            }, cancellationToken).ConfigureAwait(false);
            job.MarkTerminal(JobStatus.Error);
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
            if (job.Status is JobStatus.Success or JobStatus.Error or JobStatus.Cancelled or JobStatus.TimedOut &&
                _credentials is not null)
            {
                await _credentials.RevokeAllForJobAsync(job.JobId, CancellationToken.None).ConfigureAwait(false);
                job.SetCredentials([]);
            }

            // Stop the lease + runtime watchdogs as soon as the job reaches any terminal state
            // so neither can emit a late event or keep the run task alive.
            try { watchdogCts.Cancel(); } catch (ObjectDisposedException) { /* race on dispose */ }
            await AwaitWatchdogAsync(watchdog).ConfigureAwait(false);
            await AwaitWatchdogAsync(runtimeWatchdog).ConfigureAwait(false);
        }
    }

    private Task? StartLeaseWatchdog(Job job, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken)
    {
        // Spec §9.5.
        if (job.LeaseConstraints?.ExpiresAt is not { } expiresAt) return null;
        return RunLeaseWatchdog(job, expiresAt, emit, cancellationToken);
    }

    private Task? StartRuntimeWatchdog(Job job, CancellationToken cancellationToken)
    {
        if (job.MaxRuntimeSec is not { } limit || limit <= 0) return null;
        return RunRuntimeWatchdog(job, TimeSpan.FromSeconds(limit), cancellationToken);
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

    private static ValueTask EmitTimeoutAsync(Job job, Func<Envelope, CancellationToken, ValueTask> emit, CancellationToken cancellationToken) =>
        EmitJobErrorAsync(job, emit, new JobErrorPayload
        {
            FinalStatus = "timed_out",
            Code = ErrorCode.Timeout,
            Message = $"Job exceeded max_runtime_sec ({job.MaxRuntimeSec})",
            Retryable = true,
        }, cancellationToken);

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
            while (delay > TimeSpan.Zero &&
                   !job.CancellationToken.IsCancellationRequested &&
                   !cancellationToken.IsCancellationRequested &&
                   !IsTerminal(job.Status))
            {
                var step = delay > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
                await Task.Delay(step, _time, cancellationToken).ConfigureAwait(false);
                now = _time.GetUtcNow();
                delay = expiresAt - now;
            }
            if (cancellationToken.IsCancellationRequested || IsTerminal(job.Status)) return;
            if (!job.CancellationToken.IsCancellationRequested)
            {
                // Surface lease expiry as a status event (spec §9.5); the terminal
                // job.error{LEASE_EXPIRED, final_status:"error"} is emitted by the run-loop
                // once cancellation unwinds the agent.
                await job.EmitEventAsync(EventKinds.Status, new StatusBody
                {
                    Phase = StatusPhases.LeaseExpired,
                    Message = $"Lease expired at {expiresAt:O}",
                }, cancellationToken).ConfigureAwait(false);
                job.MarkLeaseExpired();
                job.CancellationSource.Cancel();
            }
        }
        catch (OperationCanceledException) { /* watchdog cancelled because job finished first */ }
    }

    private async Task RunRuntimeWatchdog(Job job, TimeSpan limit, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(limit, _time, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return;
            if (IsTerminal(job.Status)) return;
            // Mark the job so the run-loop knows to surface TIMEOUT not CANCELLED.
            job.MarkRuntimeLimitExceeded();
            job.CancellationSource.Cancel();
        }
        catch (OperationCanceledException) { /* watchdog cancelled because job finished first */ }
    }

    private static bool IsTerminal(JobStatus s) =>
        s is JobStatus.Success or JobStatus.Error or JobStatus.Cancelled or JobStatus.TimedOut;

    /// <summary>Cancel a running job. Only the original submitter may cancel; subscribers may not (spec §7.6).</summary>
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

    /// <summary>SHA-256 fingerprint of the submission fields that must match for an idempotent
    /// retry to be honored (spec §7.2). All JSON-shaped fields are re-serialized through the canonical
    /// <see cref="ArcpJson.Options"/> writer first so cosmetically-different inputs (whitespace,
    /// key order) round-trip to the same fingerprint.</summary>
    private static string ComputeFingerprint(JobSubmitPayload submit)
    {
        var canonical = new
        {
            agent = submit.Agent,
            input = submit.Input is null ? null : CanonicalizeJson(submit.Input.Value),
            lease_request = submit.LeaseRequest is null ? null : JsonSerializer.Serialize(submit.LeaseRequest, ArcpJson.Options),
            lease_constraints = submit.LeaseConstraints is null ? null : JsonSerializer.Serialize(submit.LeaseConstraints, ArcpJson.Options),
            parent_job_id = submit.ParentJobId,
            max_runtime_sec = submit.MaxRuntimeSec,
        };
        var json = JsonSerializer.Serialize(canonical, ArcpJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Canonicalize a <see cref="JsonElement"/> for fingerprinting: sort object keys
    /// lexicographically and re-serialize. Two inputs that parse to the same JSON value produce
    /// byte-identical output regardless of original whitespace or key order.</summary>
    private static string CanonicalizeJson(JsonElement element)
    {
        using var ms = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteCanonical(writer, element);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var props = new List<JsonProperty>();
                foreach (var p in element.EnumerateObject()) props.Add(p);
                props.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var p in props)
                {
                    writer.WritePropertyName(p.Name);
                    WriteCanonical(writer, p.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
