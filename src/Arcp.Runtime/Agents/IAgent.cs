// SPDX-License-Identifier: Apache-2.0
using System.Threading;
using System.Threading.Tasks;

namespace Arcp.Runtime.Agents;

/// <summary>The contract an agent implementation fulfills. The runtime invokes
/// <see cref="RunAsync"/> per accepted <c>job.submit</c> and uses the returned value as the
/// <c>job.result.payload.result</c> when the agent does not stream a chunked result.</summary>
public interface IAgent
{
    /// <summary>Run (asynchronous).</summary>
    Task<object?> RunAsync(JobContext context, CancellationToken cancellationToken);
}

/// <summary>A delegate-based agent for inline registration.</summary>
public sealed class DelegateAgent : IAgent
{
    private readonly System.Func<JobContext, CancellationToken, Task<object?>> _impl;

    /// <summary>Initializes a new instance of the <see cref="DelegateAgent"/> class.</summary>
    public DelegateAgent(System.Func<JobContext, CancellationToken, Task<object?>> impl)
    {
        _impl = impl;
    }

    /// <summary>Run (asynchronous).</summary>
    public Task<object?> RunAsync(JobContext context, CancellationToken cancellationToken) =>
        _impl(context, cancellationToken);
}
