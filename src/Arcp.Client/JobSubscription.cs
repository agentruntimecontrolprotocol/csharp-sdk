// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Client;

/// <summary>An active cross-session subscription to a job (spec §7.6).</summary>
public sealed class JobSubscription
{
    private readonly ArcpClient _client;
    private readonly Channel<Envelope> _events = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly TaskCompletionSource<JobSubscribedPayload> _ackTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the job id.</summary>
    public JobId JobId { get; }

    /// <summary>Gets the acknowledged.</summary>
    public Task<JobSubscribedPayload> Acknowledged => _ackTcs.Task;

    internal JobSubscription(ArcpClient client, JobId jobId)
    {
        _client = client;
        JobId = jobId;
    }

    internal void OnSubscribed(JobSubscribedPayload payload) => _ackTcs.TrySetResult(payload);

    internal void OnEvent(Envelope env) => _events.Writer.TryWrite(env);

    internal void OnTerminal() => _events.Writer.TryComplete();

    internal void HandleAcceptedFromOtherSession(JobAcceptedPayload _)
    {
        // Informational.
    }

    /// <summary>Events.</summary>
    public async IAsyncEnumerable<JobEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var env in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return JobEvent.From(env);
        }
    }

    /// <summary>Unsubscribe (asynchronous).</summary>
    public Task UnsubscribeAsync(CancellationToken cancellationToken = default) =>
        _client.UnsubscribeAsync(JobId, cancellationToken);
}
