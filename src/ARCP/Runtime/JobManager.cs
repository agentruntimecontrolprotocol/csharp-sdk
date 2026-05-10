using System.Collections.Concurrent;
using System.Text.Json;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Control;
using ARCP.Messages.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ARCP.Runtime;

/// <summary>
/// Per-job execution context handed to <see cref="ToolHandler" /> code.
/// </summary>
public sealed class JobContext
{
    private readonly JobManager _manager;

    internal JobContext(JobManager manager, JobId jobId, CancellationToken cancellationToken)
    {
        _manager = manager;
        JobId = jobId;
        CancellationToken = cancellationToken;
    }

    /// <summary>The job id assigned by the runtime.</summary>
    public JobId JobId { get; }

    /// <summary>
    /// Cancellation token that fires when the job is cancelled via
    /// <c>cancel</c>, the runtime is shut down, or the deadline elapses.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Emit a <c>job.progress</c> envelope. Does not satisfy the heartbeat
    /// contract per §10.3 — call <see cref="HeartbeatAsync" /> separately.
    /// </summary>
    /// <param name="progress">The progress payload.</param>
    /// <returns>A task that completes when the envelope is queued.</returns>
    public ValueTask ReportProgressAsync(JobProgress progress) =>
        _manager.EmitProgressAsync(JobId, progress, CancellationToken);

    /// <summary>
    /// Emit a <c>job.heartbeat</c> envelope and refresh the watchdog deadline
    /// on the runtime side.
    /// </summary>
    /// <param name="deadlineMs">Deadline in ms (defaults to capability-derived value).</param>
    /// <returns>A task that completes when the envelope is queued.</returns>
    public ValueTask HeartbeatAsync(int? deadlineMs = null) =>
        _manager.HeartbeatAsync(JobId, deadlineMs, CancellationToken);
}

/// <summary>
/// User-supplied implementation of a tool exposed via <c>tool.invoke</c>.
/// </summary>
/// <param name="invoke">The original invocation envelope payload.</param>
/// <param name="ctx">Per-job context.</param>
/// <param name="cancellationToken">Cancellation token (also surfaced via <see cref="JobContext.CancellationToken" />).</param>
/// <returns>The tool result.</returns>
public delegate Task<ToolResult> ToolHandler(
    ToolInvoke invoke,
    JobContext ctx,
    CancellationToken cancellationToken);

/// <summary>State machine record for a single in-flight job.</summary>
internal sealed class JobRecord
{
    public required JobId Id { get; init; }

    public required Ids.SessionId SessionId { get; init; }

    public required CancellationTokenSource Cts { get; init; }

    public JobState State { get; set; } = JobState.Accepted;

    public DateTimeOffset? LastHeartbeatAt { get; set; }

    public int? CurrentDeadlineMs { get; set; }

    public int MissedHeartbeats { get; set; }

    public Task? Worker { get; set; }

    public MessageId? CommandId { get; init; }

    public string? CancellationReason { get; set; }
}

/// <summary>
/// Manages durable jobs per RFC-0001-v2 §10. Each running job is a supervised
/// <see cref="Task" /> linked to the runtime's lifetime via a
/// <see cref="CancellationTokenSource" /> chain. The watchdog observes
/// per-job heartbeat timestamps and transitions stalled jobs to failed or
/// blocked per the negotiated <c>heartbeat_recovery</c> capability.
/// </summary>
public sealed class JobManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<JobId, JobRecord> _jobs = new();
    private readonly ConcurrentDictionary<string, ToolHandler> _tools;
    private readonly Func<Envelope.Envelope, CancellationToken, ValueTask> _emit;
    private readonly TimeProvider _time;
    private readonly ILogger<JobManager> _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly bool _recoveryIsBlock;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _watchdog;
    private bool _disposed;

    /// <summary>Initializes a new <see cref="JobManager" />.</summary>
    /// <param name="tools">Map of tool name to handler.</param>
    /// <param name="emit">Outbound envelope writer.</param>
    /// <param name="heartbeatIntervalSeconds">Negotiated heartbeat interval (default 30).</param>
    /// <param name="heartbeatRecovery">Negotiated recovery policy (<c>fail</c> or <c>block</c>).</param>
    /// <param name="time">Time provider (defaults to <see cref="TimeProvider.System" />).</param>
    /// <param name="logger">Optional logger.</param>
    public JobManager(
        IReadOnlyDictionary<string, ToolHandler> tools,
        Func<Envelope.Envelope, CancellationToken, ValueTask> emit,
        int heartbeatIntervalSeconds = 30,
        string heartbeatRecovery = "fail",
        TimeProvider? time = null,
        ILogger<JobManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(emit);
        _tools = new ConcurrentDictionary<string, ToolHandler>(tools);
        _emit = emit;
        _heartbeatInterval = TimeSpan.FromSeconds(heartbeatIntervalSeconds);
        _recoveryIsBlock = string.Equals(heartbeatRecovery, "block", StringComparison.Ordinal);
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<JobManager>.Instance;
        _watchdog = Task.Run(WatchdogLoopAsync);
    }

    /// <summary>Submit a new tool invocation. Returns the assigned <see cref="JobId" />.</summary>
    /// <param name="sessionId">Session id for the invocation.</param>
    /// <param name="commandId">The originating <c>tool.invoke</c> envelope id.</param>
    /// <param name="invoke">The invocation payload.</param>
    /// <param name="parentToken">Cancellation token to link to.</param>
    /// <returns>The accepted job id.</returns>
    /// <exception cref="NotFoundException">If the tool name is not registered.</exception>
    public async ValueTask<JobId> SubmitAsync(
        Ids.SessionId sessionId,
        MessageId commandId,
        ToolInvoke invoke,
        CancellationToken parentToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        if (!_tools.TryGetValue(invoke.Tool, out ToolHandler? handler))
        {
            throw new NotFoundException($"No tool handler registered for \"{invoke.Tool}\".");
        }

        JobId jobId = JobId.New();
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, parentToken);
        var record = new JobRecord
        {
            Id = jobId,
            SessionId = sessionId,
            Cts = cts,
            CommandId = commandId,
        };
        _jobs[jobId] = record;

        await EmitAsync(sessionId, jobId, "job.accepted",
            new JobAccepted(jobId, _time.GetUtcNow()),
            correlationId: commandId,
            CancellationToken.None).ConfigureAwait(false);

        record.Worker = Task.Run(() => RunAsync(record, handler, invoke), CancellationToken.None);
        return jobId;
    }

    private async Task RunAsync(JobRecord record, ToolHandler handler, ToolInvoke invoke)
    {
        var ctx = new JobContext(this, record.Id, record.Cts.Token);
        try
        {
            await SetStateAsync(record, JobState.Running).ConfigureAwait(false);
            await EmitAsync(record.SessionId, record.Id, "job.started",
                new JobStarted(record.Id, _time.GetUtcNow()),
                correlationId: null,
                CancellationToken.None).ConfigureAwait(false);

            ToolResult result = await handler(invoke, ctx, record.Cts.Token).ConfigureAwait(false);
            await SetStateAsync(record, JobState.Completed).ConfigureAwait(false);
            await EmitAsync(record.SessionId, record.Id, "job.completed",
                new JobCompleted(result.Value, result.ResultRef),
                correlationId: record.CommandId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SetStateAsync(record, JobState.Cancelled).ConfigureAwait(false);
            await EmitAsync(record.SessionId, record.Id, "job.cancelled",
                new JobCancelled(record.CancellationReason, "client"),
                correlationId: record.CommandId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (ARCPException ex)
        {
            await SetStateAsync(record, JobState.Failed).ConfigureAwait(false);
            await EmitAsync(record.SessionId, record.Id, "job.failed",
                new JobFailed(ex.Code, ex.Message, Retryable: ex.Retryable),
                correlationId: record.CommandId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} threw unexpectedly.", record.Id);
            await SetStateAsync(record, JobState.Failed).ConfigureAwait(false);
            await EmitAsync(record.SessionId, record.Id, "job.failed",
                new JobFailed(ErrorCode.Internal, ex.Message),
                correlationId: record.CommandId,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _jobs.TryRemove(record.Id, out _);
            record.Cts.Dispose();
        }
    }

    /// <summary>Process a <c>cancel</c> envelope targeting a job.</summary>
    /// <param name="commandId">The envelope id of the <c>cancel</c>.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="cancel">The cancel payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once a <c>cancel.accepted</c> or <c>cancel.refused</c> is queued.</returns>
    public async ValueTask CancelAsync(
        MessageId commandId,
        Ids.SessionId sessionId,
        Cancel cancel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        if (cancel.Target != CancelTarget.Job)
        {
            await EmitAsync(sessionId, jobId: null, "cancel.refused",
                new CancelRefused(cancel.Target, cancel.TargetId, CancelRefusedReason.NotFound),
                correlationId: commandId,
                cancellationToken).ConfigureAwait(false);
            return;
        }
        JobId targetId = JobId.FromString(cancel.TargetId);
        if (!_jobs.TryGetValue(targetId, out JobRecord? record))
        {
            await EmitAsync(sessionId, jobId: targetId, "cancel.refused",
                new CancelRefused(cancel.Target, cancel.TargetId, CancelRefusedReason.NotFound),
                correlationId: commandId,
                cancellationToken).ConfigureAwait(false);
            return;
        }
        if (IsTerminal(record.State))
        {
            await EmitAsync(sessionId, jobId: targetId, "cancel.refused",
                new CancelRefused(cancel.Target, cancel.TargetId, CancelRefusedReason.AlreadyTerminal),
                correlationId: commandId,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        record.CancellationReason = cancel.Reason;
        await EmitAsync(sessionId, jobId: targetId, "cancel.accepted",
            new CancelAccepted(cancel.Target, cancel.TargetId),
            correlationId: commandId,
            cancellationToken).ConfigureAwait(false);

        await record.Cts.CancelAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Process an <c>interrupt</c>: transition the target job to
    /// <see cref="JobState.Blocked" /> and surface that via state. The
    /// human-input handshake itself is wired in Phase 4.
    /// </summary>
    /// <param name="sessionId">Session id.</param>
    /// <param name="interrupt">Interrupt payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the interrupt found a target.</returns>
    public ValueTask<bool> InterruptAsync(
        Ids.SessionId sessionId,
        Interrupt interrupt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interrupt);
        if (interrupt.Target != CancelTarget.Job)
        {
            return ValueTask.FromResult(false);
        }
        JobId targetId = JobId.FromString(interrupt.TargetId);
        if (!_jobs.TryGetValue(targetId, out JobRecord? record))
        {
            return ValueTask.FromResult(false);
        }
        return InterruptInnerAsync(sessionId, record, cancellationToken);
    }

    private static async ValueTask<bool> InterruptInnerAsync(Ids.SessionId sessionId, JobRecord record, CancellationToken cancellationToken)
    {
        if (IsTerminal(record.State))
        {
            return false;
        }
        await SetStateAsync(record, JobState.Blocked).ConfigureAwait(false);
        // Phase 4 emits the human.input.request itself; here we just transition state.
        _ = sessionId;
        _ = cancellationToken;
        return true;
    }

    internal ValueTask EmitProgressAsync(JobId jobId, JobProgress progress, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out JobRecord? record))
        {
            return ValueTask.CompletedTask;
        }
        return EmitAsync(record.SessionId, jobId, "job.progress", progress,
            correlationId: null, cancellationToken);
    }

    internal async ValueTask HeartbeatAsync(JobId jobId, int? deadlineMs, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out JobRecord? record))
        {
            return;
        }
        record.LastHeartbeatAt = _time.GetUtcNow();
        record.MissedHeartbeats = 0;
        record.CurrentDeadlineMs = deadlineMs ?? (int)_heartbeatInterval.TotalMilliseconds;
        await EmitAsync(record.SessionId, jobId, "job.heartbeat",
            new JobHeartbeat(record.LastHeartbeatAt.Value.Ticks, record.CurrentDeadlineMs.Value, record.State),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static Task SetStateAsync(JobRecord record, JobState state)
    {
        record.State = state;
        return Task.CompletedTask;
    }

    private async Task WatchdogLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatInterval, _time, _shutdown.Token).ConfigureAwait(false);

                DateTimeOffset now = _time.GetUtcNow();
                foreach (KeyValuePair<JobId, JobRecord> pair in _jobs)
                {
                    JobRecord record = pair.Value;
                    if (record.State != JobState.Running)
                    {
                        continue;
                    }
                    if (record.LastHeartbeatAt is { } last && record.CurrentDeadlineMs is { } deadline)
                    {
                        TimeSpan elapsed = now - last;
                        if (elapsed.TotalMilliseconds > deadline)
                        {
                            record.MissedHeartbeats++;
                            if (record.MissedHeartbeats >= 2)
                            {
                                await OnHeartbeatLostAsync(record).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat watchdog terminated unexpectedly.");
        }
    }

    private async Task OnHeartbeatLostAsync(JobRecord record)
    {
        if (_recoveryIsBlock)
        {
            await SetStateAsync(record, JobState.Blocked).ConfigureAwait(false);
            return;
        }
        await SetStateAsync(record, JobState.Failed).ConfigureAwait(false);
        try
        {
            await EmitAsync(record.SessionId, record.Id, "job.failed",
                new JobFailed(ErrorCode.HeartbeatLost,
                    $"Job {record.Id} missed {record.MissedHeartbeats} consecutive heartbeats."),
                correlationId: record.CommandId,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await record.Cts.CancelAsync().ConfigureAwait(false);
        }
    }

    private static bool IsTerminal(JobState state) =>
        state is JobState.Completed or JobState.Failed or JobState.Cancelled;

    private async ValueTask EmitAsync<TPayload>(
        Ids.SessionId sessionId,
        JobId? jobId,
        string wireType,
        TPayload payload,
        MessageId? correlationId,
        CancellationToken cancellationToken)
        where TPayload : MessageType
    {
        var env = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = wireType,
            Timestamp = _time.GetUtcNow(),
            Payload = payload,
            SessionId = sessionId,
            JobId = jobId,
            CorrelationId = correlationId,
        };
        await _emit(env, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _watchdog.ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
        foreach (JobRecord record in _jobs.Values)
        {
            try
            {
                await record.Cts.CancelAsync().ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
        _shutdown.Dispose();
    }
}
