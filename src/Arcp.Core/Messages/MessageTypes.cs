// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Canonical wire type names for ARCP v1.1 messages.</summary>
public static class MessageTypeNames
{
    public const string SessionHello = "session.hello";
    public const string SessionWelcome = "session.welcome";
    public const string SessionBye = "session.bye";
    public const string SessionPing = "session.ping";
    public const string SessionPong = "session.pong";
    public const string SessionAck = "session.ack";
    public const string SessionListJobs = "session.list_jobs";
    public const string SessionJobs = "session.jobs";
    public const string SessionError = "session.error";
    public const string SessionResume = "session.resume";

    public const string JobSubmit = "job.submit";
    public const string JobAccepted = "job.accepted";
    public const string JobEvent = "job.event";
    public const string JobResult = "job.result";
    public const string JobError = "job.error";
    public const string JobCancel = "job.cancel";
    public const string JobSubscribe = "job.subscribe";
    public const string JobSubscribed = "job.subscribed";
    public const string JobUnsubscribe = "job.unsubscribe";

    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionHello, SessionWelcome, SessionBye, SessionPing, SessionPong, SessionAck,
        SessionListJobs, SessionJobs, SessionError, SessionResume,
        JobSubmit, JobAccepted, JobEvent, JobResult, JobError, JobCancel,
        JobSubscribe, JobSubscribed, JobUnsubscribe,
    }.ToFrozenSet();
}

/// <summary>Reserved event kind values carried on <c>job.event.payload.kind</c> (spec §8.2).</summary>
public static class EventKinds
{
    public const string Log = "log";
    public const string Thought = "thought";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Status = "status";
    public const string Metric = "metric";
    public const string ArtifactRef = "artifact_ref";
    public const string Delegate = "delegate";
    public const string Progress = "progress";
    public const string ResultChunk = "result_chunk";

    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Log, Thought, ToolCall, ToolResult, Status, Metric, ArtifactRef, Delegate, Progress, ResultChunk,
    }.ToFrozenSet();
}

// ---- Session messages ----

public sealed record ClientInfo
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }
}

public sealed record RuntimeInfo
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }
}

public sealed record AuthCredential
{
    [JsonPropertyName("scheme")] public string Scheme { get; init; } = "bearer";

    [JsonPropertyName("token")] public string? Token { get; init; }
}

public sealed record SessionHelloPayload
{
    [JsonPropertyName("client")] public required ClientInfo Client { get; init; }

    [JsonPropertyName("auth")] public AuthCredential? Auth { get; init; }

    [JsonPropertyName("capabilities")] public required Capabilities Capabilities { get; init; }

    /// <summary>If present, the runtime treats this as a resume attempt (spec §6.3).</summary>
    [JsonPropertyName("resume_token")] public string? ResumeToken { get; init; }

    [JsonPropertyName("last_event_seq")] public long? LastEventSeq { get; init; }
}

public sealed record SessionWelcomePayload
{
    [JsonPropertyName("runtime")] public required RuntimeInfo Runtime { get; init; }

    [JsonPropertyName("resume_token")] public string? ResumeToken { get; init; }

    [JsonPropertyName("resume_window_sec")] public int? ResumeWindowSec { get; init; }

    [JsonPropertyName("heartbeat_interval_sec")] public int? HeartbeatIntervalSec { get; init; }

    [JsonPropertyName("capabilities")] public required Capabilities Capabilities { get; init; }
}

public sealed record SessionByePayload
{
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record SessionPingPayload
{
    [JsonPropertyName("nonce")] public required string Nonce { get; init; }

    [JsonPropertyName("sent_at")] public required DateTimeOffset SentAt { get; init; }
}

public sealed record SessionPongPayload
{
    [JsonPropertyName("ping_nonce")] public required string PingNonce { get; init; }

    [JsonPropertyName("received_at")] public required DateTimeOffset ReceivedAt { get; init; }
}

public sealed record SessionAckPayload
{
    [JsonPropertyName("last_processed_seq")] public required long LastProcessedSeq { get; init; }
}

public sealed record SessionListJobsPayload
{
    [JsonPropertyName("filter")] public JobListFilter? Filter { get; init; }

    [JsonPropertyName("limit")] public int? Limit { get; init; }

    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
}

public sealed record JobListFilter
{
    [JsonPropertyName("status")] public IReadOnlyList<string>? Status { get; init; }

    [JsonPropertyName("agent")] public string? Agent { get; init; }

    [JsonPropertyName("created_after")] public DateTimeOffset? CreatedAfter { get; init; }
}

public sealed record SessionJobsPayload
{
    [JsonPropertyName("request_id")] public string? RequestId { get; init; }

    [JsonPropertyName("jobs")] public required IReadOnlyList<JobListEntry> Jobs { get; init; }

    [JsonPropertyName("next_cursor")] public string? NextCursor { get; init; }
}

public sealed record JobListEntry
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    [JsonPropertyName("last_event_seq")] public long? LastEventSeq { get; init; }
}

public sealed record SessionErrorPayload
{
    [JsonPropertyName("code")] public required string Code { get; init; }

    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("retryable")] public bool Retryable { get; init; }

    [JsonPropertyName("detail")] public string? Detail { get; init; }
}

// ---- Job messages ----

public sealed record JobSubmitPayload
{
    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("input")] public JsonElement? Input { get; init; }

    [JsonPropertyName("lease_request")] public Lease? LeaseRequest { get; init; }

    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    [JsonPropertyName("idempotency_key")] public string? IdempotencyKey { get; init; }

    [JsonPropertyName("max_runtime_sec")] public int? MaxRuntimeSec { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }
}

public sealed record JobAcceptedPayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    [JsonPropertyName("budget")] public IReadOnlyDictionary<string, decimal>? Budget { get; init; }

    [JsonPropertyName("accepted_at")] public required DateTimeOffset AcceptedAt { get; init; }

    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }
}

public sealed record JobEventPayload
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("ts")] public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("body")] public JsonElement Body { get; init; }
}

public sealed record JobResultPayload
{
    [JsonPropertyName("final_status")] public string FinalStatus { get; init; } = "success";

    [JsonPropertyName("result")] public JsonElement? Result { get; init; }

    [JsonPropertyName("result_id")] public string? ResultId { get; init; }

    [JsonPropertyName("result_size")] public long? ResultSize { get; init; }

    [JsonPropertyName("summary")] public string? Summary { get; init; }
}

public sealed record JobErrorPayload
{
    [JsonPropertyName("final_status")] public string FinalStatus { get; init; } = "error";

    [JsonPropertyName("code")] public required string Code { get; init; }

    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("retryable")] public bool Retryable { get; init; }

    [JsonPropertyName("detail")] public string? Detail { get; init; }
}

public sealed record JobCancelPayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record JobSubscribePayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("from_event_seq")] public long? FromEventSeq { get; init; }

    [JsonPropertyName("history")] public bool History { get; init; }
}

public sealed record JobSubscribedPayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }

    [JsonPropertyName("current_status")] public required string CurrentStatus { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("lease")] public Lease? Lease { get; init; }

    [JsonPropertyName("lease_constraints")] public LeaseConstraints? LeaseConstraints { get; init; }

    [JsonPropertyName("parent_job_id")] public string? ParentJobId { get; init; }

    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    [JsonPropertyName("subscribed_from")] public long? SubscribedFrom { get; init; }

    [JsonPropertyName("replayed")] public bool Replayed { get; init; }
}

public sealed record JobUnsubscribePayload
{
    [JsonPropertyName("job_id")] public required string JobId { get; init; }
}

// ---- Event body shapes ----

public sealed record LogBody
{
    [JsonPropertyName("level")] public required string Level { get; init; }

    [JsonPropertyName("message")] public required string Message { get; init; }
}

public sealed record ThoughtBody
{
    [JsonPropertyName("text")] public required string Text { get; init; }
}

public sealed record ToolCallBody
{
    [JsonPropertyName("tool")] public required string Tool { get; init; }

    [JsonPropertyName("call_id")] public required string CallId { get; init; }

    [JsonPropertyName("args")] public JsonElement? Args { get; init; }
}

public sealed record ToolResultBody
{
    [JsonPropertyName("call_id")] public required string CallId { get; init; }

    [JsonPropertyName("result")] public JsonElement? Result { get; init; }

    [JsonPropertyName("error")] public ToolError? Error { get; init; }
}

public sealed record ToolError
{
    [JsonPropertyName("code")] public required string Code { get; init; }

    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("retryable")] public bool Retryable { get; init; }
}

public sealed record StatusBody
{
    [JsonPropertyName("phase")] public required string Phase { get; init; }

    [JsonPropertyName("message")] public string? Message { get; init; }
}

public sealed record MetricBody
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("value")] public required double Value { get; init; }

    [JsonPropertyName("unit")] public string? Unit { get; init; }

    [JsonPropertyName("dimensions")] public IReadOnlyDictionary<string, string>? Dimensions { get; init; }
}

public sealed record ArtifactRefBody
{
    [JsonPropertyName("uri")] public required string Uri { get; init; }

    [JsonPropertyName("content_type")] public string? ContentType { get; init; }

    [JsonPropertyName("byte_size")] public long? ByteSize { get; init; }

    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
}

public sealed record DelegateBody
{
    [JsonPropertyName("child_job_id")] public required string ChildJobId { get; init; }

    [JsonPropertyName("agent")] public required string Agent { get; init; }

    [JsonPropertyName("input")] public JsonElement? Input { get; init; }
}

public sealed record ProgressBody
{
    [JsonPropertyName("current")] public required long Current { get; init; }

    [JsonPropertyName("total")] public long? Total { get; init; }

    [JsonPropertyName("units")] public string? Units { get; init; }

    [JsonPropertyName("message")] public string? Message { get; init; }

    public ProgressBody Validate()
    {
        if (Current < 0)
            throw new Errors.InvalidRequestException("progress.current MUST be ≥ 0 (spec §8.2.1)");
        if (Total is { } t && Current > t)
            throw new Errors.InvalidRequestException("progress.current SHOULD be ≤ total (spec §8.2.1)");
        return this;
    }
}

public sealed record ResultChunkBody
{
    [JsonPropertyName("result_id")] public required string ResultId { get; init; }

    [JsonPropertyName("chunk_seq")] public required long ChunkSeq { get; init; }

    [JsonPropertyName("data")] public required string Data { get; init; }

    [JsonPropertyName("encoding")] public required string Encoding { get; init; }

    [JsonPropertyName("more")] public required bool More { get; init; }
}
