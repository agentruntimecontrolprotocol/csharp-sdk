// SPDX-License-Identifier: Apache-2.0
using System;
using System.Buffers;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

/// <summary>The execution context handed to <see cref="Agents.IAgent.RunAsync"/>. Exposes
/// one method per reserved event kind (spec §8.2).</summary>
public sealed class JobContext
{
    private readonly Job _job;

    internal JobContext(Job job, ILogger logger)
    {
        _job = job;
        Logger = logger;
    }

    public JobId JobId => _job.JobId;

    public SessionId SessionId => _job.SessionId;

    public AgentRef Agent => _job.Agent;

    public Lease Lease => _job.Lease;

    public LeaseConstraints? LeaseConstraints => _job.LeaseConstraints;

    public IReadOnlyDictionary<string, decimal> Budget => _job.BudgetLedger.Remaining;

    public TraceId? TraceId => _job.TraceId;

    public JsonElement? Input => _job.Input;

    public ILogger Logger { get; }

    public CancellationToken Cancellation => _job.CancellationToken;

    public ValueTask LogAsync(string level, string message, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Log, new LogBody { Level = level, Message = message }, cancellationToken);

    public ValueTask ThoughtAsync(string text, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Thought, new ThoughtBody { Text = text }, cancellationToken);

    public ValueTask StatusAsync(string phase, string? message = null, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Status, new StatusBody { Phase = phase, Message = message }, cancellationToken);

    public ValueTask ToolCallAsync(string tool, string callId, object? args, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.ToolCall, new ToolCallBody
        {
            Tool = tool,
            CallId = callId,
            Args = args is null ? null : ArcpJson.ToJsonElement(args),
        }, cancellationToken);

    public ValueTask ToolResultAsync(string callId, object? result, ToolError? error = null, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.ToolResult, new ToolResultBody
        {
            CallId = callId,
            Result = result is null ? null : ArcpJson.ToJsonElement(result),
            Error = error,
        }, cancellationToken);

    public ValueTask MetricAsync(string name, double value, string? unit = null, IReadOnlyDictionary<string, string>? dimensions = null, CancellationToken cancellationToken = default) =>
        _job.EmitMetricAsync(new MetricBody { Name = name, Value = value, Unit = unit, Dimensions = dimensions }, cancellationToken);

    public ValueTask ArtifactRefAsync(string uri, string? contentType = null, long? byteSize = null, string? sha256 = null, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.ArtifactRef, new ArtifactRefBody
        {
            Uri = uri,
            ContentType = contentType,
            ByteSize = byteSize,
            Sha256 = sha256,
        }, cancellationToken);

    public ValueTask DelegateAsync(string childJobId, string agent, object? input, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Delegate, new DelegateBody
        {
            ChildJobId = childJobId,
            Agent = agent,
            Input = input is null ? null : ArcpJson.ToJsonElement(input),
        }, cancellationToken);

    /// <summary>Emit a <c>progress</c> event (spec §8.2.1).</summary>
    public ValueTask ProgressAsync(long current, long? total = null, string? units = null, string? message = null, CancellationToken cancellationToken = default)
    {
        var body = new ProgressBody { Current = current, Total = total, Units = units, Message = message }.Validate();
        return _job.EmitEventAsync(EventKinds.Progress, body, cancellationToken);
    }

    /// <summary>Begin a streamed result. Returns a stable <c>result_id</c> the runtime will reference
    /// from the terminating <c>job.result</c> (spec §8.4).</summary>
    public ResultId BeginResultStream() => _job.BeginResultStream();

    /// <summary>Emit one <c>result_chunk</c> with UTF-8 text payload (spec §8.4).</summary>
    public ValueTask WriteChunkAsync(ResultId resultId, string text, bool more, CancellationToken cancellationToken = default) =>
        _job.WriteChunkAsync(resultId, text, encoding: "utf8", more, cancellationToken);

    /// <summary>Emit one <c>result_chunk</c> with binary payload, base64-encoded (spec §8.4).</summary>
    public ValueTask WriteChunkAsync(ResultId resultId, ReadOnlyMemory<byte> bytes, bool more, CancellationToken cancellationToken = default) =>
        _job.WriteChunkAsync(resultId, Convert.ToBase64String(bytes.Span), encoding: "base64", more, cancellationToken);

    /// <summary>Emit a vendor-namespaced event (key SHOULD start with <c>x-vendor.</c>).</summary>
    public ValueTask EmitEventAsync(string kind, object body, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(kind, body, cancellationToken);
}
