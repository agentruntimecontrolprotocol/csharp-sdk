using System.Text.Json;
using System.Text.Json.Serialization;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;

namespace ARCP.Messages.Execution;

/// <summary>§16.1 canonical artifact reference (re-used in tool/job results).</summary>
/// <param name="ArtifactId">Stable artifact id.</param>
/// <param name="Uri">Logical URI (e.g. <c>arcp://session/&lt;sid&gt;/artifact/&lt;aid&gt;</c>).</param>
/// <param name="MediaType">IANA media type (e.g. <c>application/json</c>).</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Optional SHA-256 of contents.</param>
/// <param name="ExpiresAt">Optional retention expiry.</param>
public sealed record ArtifactRef(
    Ids.ArtifactId ArtifactId,
    string Uri,
    string MediaType,
    long Size,
    string? Sha256 = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>§10 / §6.2 invoke a tool.</summary>
public sealed record ToolInvoke(
    string Tool,
    JsonElement? Arguments = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "tool.invoke";
}

/// <summary>§6.3 successful tool result.</summary>
public sealed record ToolResult(
    JsonElement? Value = null,
    ArtifactRef? ResultRef = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "tool.result";
}

/// <summary>§18 tool error.</summary>
public sealed record ToolError(
    ErrorCode Code,
    string Message,
    bool? Retryable = null,
    IReadOnlyDictionary<string, JsonElement>? Details = null,
    string? TraceId = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "tool.error";
}

/// <summary>§10.2 job state.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JobState>))]
public enum JobState
{
    /// <summary>Runtime accepted the command but has not started work.</summary>
    [JsonStringEnumMemberName("accepted")]
    Accepted,

    /// <summary>Work is waiting for capacity, permissions, or dependencies.</summary>
    [JsonStringEnumMemberName("queued")]
    Queued,

    /// <summary>Work is actively executing.</summary>
    [JsonStringEnumMemberName("running")]
    Running,

    /// <summary>Work is waiting on an external event, permission, or human input.</summary>
    [JsonStringEnumMemberName("blocked")]
    Blocked,

    /// <summary>Work was intentionally suspended and can be resumed.</summary>
    [JsonStringEnumMemberName("paused")]
    Paused,

    /// <summary>Work finished successfully (terminal).</summary>
    [JsonStringEnumMemberName("completed")]
    Completed,

    /// <summary>Work reached a terminal error.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>Work was cancelled (terminal).</summary>
    [JsonStringEnumMemberName("cancelled")]
    Cancelled,
}

/// <summary>§10 job accepted by the runtime; <c>job_id</c> assigned.</summary>
public sealed record JobAccepted(
    Ids.JobId JobId,
    DateTimeOffset AcceptedAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.accepted";
}

/// <summary>§10 job started executing.</summary>
public sealed record JobStarted(
    Ids.JobId JobId,
    DateTimeOffset StartedAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.started";
}

/// <summary>§10.1 job progress update.</summary>
public sealed record JobProgress(
    double? Percent = null,
    string? Message = null,
    long? Current = null,
    long? Total = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.progress";
}

/// <summary>§10.3 heartbeat tick.</summary>
public sealed record JobHeartbeat(
    long Sequence,
    int DeadlineMs,
    JobState State) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.heartbeat";
}

/// <summary>§10.1 / §19 job checkpoint snapshot. Parse-only in v0.1.</summary>
public sealed record JobCheckpoint(
    string CheckpointId,
    JsonElement? Snapshot = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.checkpoint";
}

/// <summary>§10 job completed successfully (terminal).</summary>
public sealed record JobCompleted(
    JsonElement? Result = null,
    ArtifactRef? ResultRef = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.completed";
}

/// <summary>§10 / §18 job failed (terminal).</summary>
public sealed record JobFailed(
    ErrorCode Code,
    string Message,
    bool? Retryable = null,
    IReadOnlyDictionary<string, JsonElement>? Details = null,
    string? TraceId = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.failed";
}

/// <summary>§10.4 job cancelled (terminal).</summary>
public sealed record JobCancelled(
    string? Reason = null,
    string? Source = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.cancelled";
}

/// <summary>§10.6 schedule a deferred or recurring job (parse-only in v0.1).</summary>
public sealed record JobSchedule(
    JsonElement Job,
    JsonElement When) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "job.schedule";
}

/// <summary>§14 / §10 stub: workflow start (parse-only in v0.1).</summary>
public sealed record WorkflowStart(
    string Workflow,
    IReadOnlyDictionary<string, JsonElement>? Inputs = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "workflow.start";
}

/// <summary>§14 / §10 stub: workflow complete (parse-only in v0.1).</summary>
public sealed record WorkflowComplete(
    IReadOnlyDictionary<string, JsonElement>? Outputs = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "workflow.complete";
}

/// <summary>§14 stub: agent delegation (parse-only in v0.1).</summary>
public sealed record AgentDelegate(
    string Target,
    string Task,
    JsonElement? Context = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "agent.delegate";
}

/// <summary>§14 stub: agent handoff (parse-only in v0.1).</summary>
public sealed record AgentHandoff(
    Session.RuntimeIdentity ToRuntime,
    Ids.JobId? JobId = null,
    Ids.SessionId? SessionId = null,
    string? Reason = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "agent.handoff";
}
