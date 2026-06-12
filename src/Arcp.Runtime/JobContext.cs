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
using Arcp.Runtime.Credentials;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime;

/// <summary>The execution context handed to <see cref="Agents.IAgent.RunAsync"/>. Exposes
/// one method per reserved event kind (spec §8.2).</summary>
public sealed class JobContext
{
    private readonly Job _job;
    private readonly CredentialManager? _credentials;
    private readonly bool _fatalBudgetExhaustion;
    private readonly Arcp.Runtime.Leases.LeaseManager? _leases;
    private readonly bool _permissiveUnleasedOperations;

    internal JobContext(Job job, ILogger logger, CredentialManager? credentials = null,
                        bool fatalBudgetExhaustion = false, Arcp.Runtime.Leases.LeaseManager? leases = null,
                        bool permissiveUnleasedOperations = false)
    {
        _job = job;
        _credentials = credentials;
        _fatalBudgetExhaustion = fatalBudgetExhaustion;
        _leases = leases;
        _permissiveUnleasedOperations = permissiveUnleasedOperations;
        Logger = logger;
    }

    /// <summary>Synchronously evaluate the job's lease for an operation under
    /// <paramref name="namespaceName"/> against <paramref name="pattern"/> (spec §9.3).
    /// Throws <see cref="PermissionDeniedException"/> on lease miss and
    /// <see cref="LeaseExpiredException"/> on lease expiry. Agents that build their own tool
    /// dispatch SHOULD call this before performing any authority-bearing operation.</summary>
    public void AuthorizeOperation(string namespaceName, string pattern)
    {
        var leases = _leases ?? new Arcp.Runtime.Leases.LeaseManager();
        // Spec §9.6: pass the job's budget ledger so the operation is gated on remaining budget
        // before the capability/pattern check.
        leases.AuthorizeOperation(_job.Lease, _job.LeaseConstraints, namespaceName, pattern, _job.BudgetLedger);
    }

    /// <summary>Gets the job id.</summary>
    public JobId JobId => _job.JobId;

    /// <summary>Gets the session id.</summary>
    public SessionId SessionId => _job.SessionId;

    /// <summary>Gets the agent.</summary>
    public AgentRef Agent => _job.Agent;

    /// <summary>Gets the lease.</summary>
    public Lease Lease => _job.Lease;

    /// <summary>Gets the lease constraints.</summary>
    public LeaseConstraints? LeaseConstraints => _job.LeaseConstraints;

    /// <summary>Gets the budget.</summary>
    public IReadOnlyDictionary<string, decimal> Budget => _job.BudgetLedger.Remaining;

    /// <summary>Gets the credentials.</summary>
    public IReadOnlyList<ProvisionedCredential> Credentials =>
        CredentialRedaction.StripValues(_job.Credentials);

    /// <summary>Gets the trace id.</summary>
    public TraceId? TraceId => _job.TraceId;

    /// <summary>Gets the input.</summary>
    public JsonElement? Input => _job.Input;

    /// <summary>Gets the logger.</summary>
    public ILogger Logger { get; }

    /// <summary>Gets the cancellation.</summary>
    public CancellationToken Cancellation => _job.CancellationToken;

    /// <summary>Log (asynchronous).</summary>
    public ValueTask LogAsync(string level, string message, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Log, new LogBody { Level = level, Message = message }, cancellationToken);

    /// <summary>Thought (asynchronous).</summary>
    public ValueTask ThoughtAsync(string text, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Thought, new ThoughtBody { Text = text }, cancellationToken);

    /// <summary>Status (asynchronous).</summary>
    public ValueTask StatusAsync(string phase, string? message = null, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.Status, new StatusBody { Phase = phase, Message = message }, cancellationToken);

    /// <summary>Rotate credential (asynchronous).</summary>
    public ValueTask RotateCredentialAsync(
        string credentialId,
        ProvisionedCredential next,
        CancellationToken cancellationToken = default)
    {
        if (_credentials is null)
            throw new InvalidRequestException("Credential rotation requires a configured credential provisioner.");
        return _credentials.RotateAsync(_job, credentialId, next, cancellationToken);
    }

    /// <summary>Emit a <c>tool_call</c> event. If the job's lease declares <c>tool.call</c>, the
    /// tool name is gated against the lease patterns first (spec §9.3); a non-matching tool
    /// raises <see cref="PermissionDeniedException"/> before the event is emitted.</summary>
    public ValueTask ToolCallAsync(string tool, string callId, object? args, CancellationToken cancellationToken = default)
    {
        EnforceLeaseCoverage(LeaseNamespaces.ToolCall, tool);
        return _job.EmitEventAsync(EventKinds.ToolCall, new ToolCallBody
        {
            Tool = tool,
            CallId = callId,
            Args = args is null ? null : ArcpJson.ToJsonElement(args),
        }, cancellationToken);
    }

    /// <summary>Gate an authority-bearing operation against the lease. Spec §9.1/§9.3 require
    /// deny-by-default: an operation whose namespace the lease does not declare is unauthorized and
    /// raises <see cref="PermissionDeniedException"/>. Opt into the legacy permissive behavior (allow
    /// uncovered namespaces) via <see cref="ArcpServerOptions.PermissiveUnleasedOperations"/>.</summary>
    private void EnforceLeaseCoverage(string namespaceName, string pattern)
    {
        if (_job.Lease.Capabilities.ContainsKey(namespaceName))
        {
            AuthorizeOperation(namespaceName, pattern);
            return;
        }

        if (!_permissiveUnleasedOperations)
        {
            throw new PermissionDeniedException(
                $"Operation '{namespaceName}' is not covered by the job's lease (deny-by-default, spec §9.3). " +
                "Grant the namespace in the lease, or enable ArcpServerOptions.PermissiveUnleasedOperations.");
        }
    }

    /// <summary>Tool result (asynchronous).</summary>
    public ValueTask ToolResultAsync(string callId, object? result, ToolError? error = null, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.ToolResult, new ToolResultBody
        {
            CallId = callId,
            Result = result is null ? null : ArcpJson.ToJsonElement(result),
            Error = error,
        }, cancellationToken);

    /// <summary>Emit a <c>metric</c> event. If <paramref name="name"/> begins with <c>cost.</c> and
    /// <paramref name="unit"/> matches a budgeted currency, the budget counter is decremented
    /// (spec §9.6). On exhaustion the runtime surfaces a non-fatal <c>tool_result.error</c> with
    /// code <c>BUDGET_EXHAUSTED</c> so the agent may continue with non-cost-bearing operations
    /// (spec §9.6 SHOULD); set <see cref="ArcpServerOptions.FatalBudgetExhaustion"/> to opt into
    /// legacy fatal termination.</summary>
    public async ValueTask MetricAsync(string name, double value, string? unit = null, IReadOnlyDictionary<string, string>? dimensions = null, CancellationToken cancellationToken = default)
    {
        var exhaustedCurrency = await _job
            .EmitMetricAsync(new MetricBody { Name = name, Value = value, Unit = unit, Dimensions = dimensions }, cancellationToken)
            .ConfigureAwait(false);
        if (exhaustedCurrency is null) return;

        var message = $"{exhaustedCurrency} budget exhausted (remaining≤0)";
        if (_fatalBudgetExhaustion)
            throw new BudgetExhaustedException(message);

        await ToolResultAsync(
            callId: $"budget_{exhaustedCurrency}",
            result: null,
            error: new ToolError
            {
                Code = ErrorCode.BudgetExhausted,
                Message = message,
                Retryable = false,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Artifact ref (asynchronous).</summary>
    public ValueTask ArtifactRefAsync(string uri, string? contentType = null, long? byteSize = null, string? sha256 = null, CancellationToken cancellationToken = default) =>
        _job.EmitEventAsync(EventKinds.ArtifactRef, new ArtifactRefBody
        {
            Uri = uri,
            ContentType = contentType,
            ByteSize = byteSize,
            Sha256 = sha256,
        }, cancellationToken);

    /// <summary>Emit a <c>delegate</c> event. If the job's lease declares <c>agent.delegate</c>, the
    /// child agent name is gated against the lease patterns first (spec §9.3, §10).</summary>
    public ValueTask DelegateAsync(string childJobId, string agent, object? input, CancellationToken cancellationToken = default)
    {
        EnforceLeaseCoverage(LeaseNamespaces.AgentDelegate, agent);
        return _job.EmitEventAsync(EventKinds.Delegate, new DelegateBody
        {
            ChildJobId = childJobId,
            Agent = agent,
            Input = input is null ? null : ArcpJson.ToJsonElement(input),
        }, cancellationToken);
    }

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
