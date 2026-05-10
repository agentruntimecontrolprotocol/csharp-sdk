using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;

namespace ARCP.Messages.Control;

/// <summary>§6.2 keep-alive request.</summary>
public sealed record Ping : MessageType
{
    /// <inheritdoc />
    public override string WireType => "ping";
}

/// <summary>§6.2 keep-alive reply.</summary>
public sealed record Pong(MessageId AckFor, DateTimeOffset ReceivedAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "pong";
}

/// <summary>§6.2 generic acknowledgement.</summary>
public sealed record Ack(MessageId AckFor, DateTimeOffset ReceivedAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "ack";
}

/// <summary>§6.2 / §18 negative acknowledgement carrying an error payload.</summary>
public sealed record Nack(
    ErrorCode Code,
    string Message,
    MessageId? AckFor = null,
    bool? Retryable = null,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Details = null,
    string? TraceId = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "nack";
}

/// <summary>The kind of object a <see cref="Cancel" /> targets.</summary>
public enum CancelTarget
{
    /// <summary>Target is a job (§10).</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("job")]
    Job,

    /// <summary>Target is a stream (§11).</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("stream")]
    Stream,

    /// <summary>Target is a session (§9).</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("session")]
    Session,
}

/// <summary>§10.4 cancellation request.</summary>
public sealed record Cancel(
    CancelTarget Target,
    string TargetId,
    string? Reason = null,
    int? DeadlineMs = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "cancel";
}

/// <summary>§10.4 cancellation accepted; terminal event will follow.</summary>
public sealed record CancelAccepted(
    CancelTarget Target,
    string TargetId) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "cancel.accepted";
}

/// <summary>Reason a cancel was refused (§10.4).</summary>
public enum CancelRefusedReason
{
    /// <summary>Target cannot be cancelled in its current state.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("not_cancellable")]
    NotCancellable,

    /// <summary>Target has already reached a terminal state.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("already_terminal")]
    AlreadyTerminal,

    /// <summary>Target id does not refer to a known entity.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("not_found")]
    NotFound,
}

/// <summary>§10.4 cancellation refused.</summary>
public sealed record CancelRefused(
    CancelTarget Target,
    string TargetId,
    CancelRefusedReason Reason) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "cancel.refused";
}

/// <summary>§10.5 interrupt request: pause and accept human guidance.</summary>
public sealed record Interrupt(
    CancelTarget Target,
    string TargetId,
    string? Prompt = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "interrupt";
}

/// <summary>§19 client &gt; runtime: resume a previously open session.</summary>
public sealed record Resume(
    MessageId? AfterMessageId = null,
    string? CheckpointId = null,
    bool? IncludeOpenStreams = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "resume";
}

/// <summary>§11.2 backpressure signal.</summary>
public sealed record Backpressure(
    double? DesiredRatePerSecond = null,
    long? BufferRemainingBytes = null,
    string? Reason = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "backpressure";
}

/// <summary>§19 stub: checkpoint creation. Phase-1 parse-only.</summary>
public sealed record CheckpointCreate(
    string? CheckpointId = null,
    System.Text.Json.JsonElement? Snapshot = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "checkpoint.create";
}

/// <summary>§19 stub: checkpoint restore. Phase-1 parse-only.</summary>
public sealed record CheckpointRestore(string CheckpointId) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "checkpoint.restore";
}
