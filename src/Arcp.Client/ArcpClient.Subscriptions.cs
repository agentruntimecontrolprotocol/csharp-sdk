// SPDX-License-Identifier: Apache-2.0
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Client;

public sealed partial class ArcpClient
{
    /// <summary>Subscribe (asynchronous).</summary>
    public async Task<JobSubscription> SubscribeAsync(JobId jobId, bool history = false,
        long? fromEventSeq = null, CancellationToken cancellationToken = default)
    {
        var sub = new JobSubscription(this, jobId);
        _subscriptions[jobId] = sub;
        try
        {
            await _transport.SendAsync(new Envelope
            {
                Type = MessageTypeNames.JobSubscribe,
                SessionId = SessionId.Value,
                JobId = jobId.Value,
                Payload = new JobSubscribePayload { JobId = jobId.Value, History = history, FromEventSeq = fromEventSeq },
            }, cancellationToken).ConfigureAwait(false);
            await sub.Acknowledged.WaitAsync(cancellationToken).ConfigureAwait(false);
            return sub;
        }
        catch
        {
            _subscriptions.TryRemove(jobId, out _);
            throw;
        }
    }

    /// <summary>Unsubscribe (asynchronous).</summary>
    public async Task UnsubscribeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        _subscriptions.TryRemove(jobId, out _);
        await _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobUnsubscribe,
            SessionId = SessionId.Value,
            JobId = jobId.Value,
            Payload = new JobUnsubscribePayload { JobId = jobId.Value },
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ack (asynchronous).</summary>
    public ValueTask AckAsync(long lastProcessedSeq, CancellationToken cancellationToken = default) =>
        _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionAck,
            SessionId = SessionId.Value,
            Payload = new SessionAckPayload { LastProcessedSeq = lastProcessedSeq },
        }, cancellationToken);
}
