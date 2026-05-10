using System.Collections.Concurrent;
using System.Threading.Channels;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Subscriptions;
using ARCP.Store;

namespace ARCP.Runtime;

/// <summary>
/// Live observer record.
/// </summary>
internal sealed class SubscriptionRecord
{
    public required SubscriptionId Id { get; init; }

    public required Ids.SessionId SubscriberSessionId { get; init; }

    public required SubscribeFilter Filter { get; init; }

    public required Channel<Envelope.Envelope> Outbound { get; init; }
}

/// <summary>
/// Manages observers per RFC-0001-v2 §13. Each subscription is backed by a
/// bounded <see cref="Channel{T}" /> so backpressure on the consumer side
/// applies cleanly. Backfill is performed by reading the configured
/// <see cref="EventLog" /> up to the boundary; the boundary marker
/// <c>subscription.backfill_complete</c> is delivered as a synthetic
/// <c>subscribe.event</c> per §13.3.
/// </summary>
public sealed class SubscriptionManager
{
    private readonly ConcurrentDictionary<SubscriptionId, SubscriptionRecord> _subs = new();
    private readonly EventLog _log;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new <see cref="SubscriptionManager" />.</summary>
    /// <param name="log">Event log used for backfill.</param>
    /// <param name="time">Optional time provider.</param>
    public SubscriptionManager(EventLog log, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Open a new subscription. Authorization is enforced at filter compile
    /// time per §13.2: the subscriber's <paramref name="subscriberSessionId" />
    /// must appear in <see cref="SubscribeFilter.SessionId" /> when the filter
    /// references other sessions.
    /// </summary>
    /// <param name="subscriberSessionId">Session id of the subscriber.</param>
    /// <param name="subscribe">Subscription request.</param>
    /// <param name="capacity">Bounded channel capacity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new subscription id and an async iterator of envelopes.</returns>
    /// <exception cref="PermissionDeniedException">If the filter references session ids the subscriber is not authorized to observe.</exception>
    public async Task<(SubscriptionId Id, IAsyncEnumerable<Envelope.Envelope> Stream)> SubscribeAsync(
        Ids.SessionId subscriberSessionId,
        Subscribe subscribe,
        int capacity = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscribe);

        // §13.2 authorization: cross-session subscription requires that the subscriber's
        // session id is in the requested filter (or the filter is unrestricted, in which
        // case we restrict to the subscriber's own session).
        SubscribeFilter filter = subscribe.Filter;
        if (filter.SessionId is { Count: > 0 } sessions)
        {
            if (!sessions.Contains(subscriberSessionId.Value, StringComparer.Ordinal))
            {
                throw new PermissionDeniedException(
                    "Cross-session subscriptions require the subscriber's session_id in filter.session_id (§13.2).");
            }
        }
        else
        {
            filter = filter with { SessionId = new[] { subscriberSessionId.Value } };
        }

        SubscriptionId id = SubscriptionId.New();
        Channel<Envelope.Envelope> channel = Channel.CreateBounded<Envelope.Envelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        var record = new SubscriptionRecord
        {
            Id = id,
            SubscriberSessionId = subscriberSessionId,
            Filter = filter,
            Outbound = channel,
        };
        _subs[id] = record;

        // Build the async iterator combining backfill + live tail.
        async IAsyncEnumerable<Envelope.Envelope> Iterator(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            // Backfill from the event log (§13.3) when "since" is supplied.
            if (subscribe.Since?.AfterMessageId is { } anchor)
            {
                MessageId anchorId = MessageId.FromString(anchor);
                foreach (string sessionStr in record.Filter.SessionId!)
                {
                    Ids.SessionId session = Ids.SessionId.FromString(sessionStr);
                    await foreach (EventLogEntry entry in _log.ReplayAsync(session, anchorId, ct).ConfigureAwait(false))
                    {
                        if (Matches(entry.Envelope, record.Filter))
                        {
                            yield return entry.Envelope;
                        }
                    }
                }
                yield return BuildBackfillCompleteMarker(record);
            }

            await foreach (Envelope.Envelope env in record.Outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return env;
            }
        }

        return (id, Iterator(cancellationToken));
    }

    /// <summary>
    /// Publish an envelope to all matching subscribers.
    /// </summary>
    /// <param name="envelope">Envelope to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the envelope has been queued to all matching subscriptions.</returns>
    public async Task PublishAsync(Envelope.Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        foreach (SubscriptionRecord record in _subs.Values)
        {
            if (Matches(envelope, record.Filter))
            {
                try
                {
                    await record.Outbound.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    _subs.TryRemove(record.Id, out _);
                }
            }
        }
    }

    /// <summary>Close a subscription cleanly.</summary>
    /// <param name="id">Subscription id.</param>
    public void Unsubscribe(SubscriptionId id)
    {
        if (_subs.TryRemove(id, out SubscriptionRecord? record))
        {
            record.Outbound.Writer.TryComplete();
        }
    }

    private static bool Matches(Envelope.Envelope env, SubscribeFilter filter)
    {
        if (filter.SessionId is { Count: > 0 } sessions
            && env.SessionId is { } sessionId
            && !sessions.Contains(sessionId.Value, StringComparer.Ordinal))
        {
            return false;
        }
        if (filter.JobId is { Count: > 0 } jobs
            && env.JobId is { } jobId
            && !jobs.Contains(jobId.Value, StringComparer.Ordinal))
        {
            return false;
        }
        if (filter.StreamId is { Count: > 0 } streams
            && env.StreamId is { } streamId
            && !streams.Contains(streamId.Value, StringComparer.Ordinal))
        {
            return false;
        }
        if (filter.TraceId is { Count: > 0 } traces
            && env.TraceId is { } traceId
            && !traces.Contains(traceId.Value, StringComparer.Ordinal))
        {
            return false;
        }
        if (filter.Types is { Count: > 0 } types
            && !types.Contains(env.Type, StringComparer.Ordinal))
        {
            return false;
        }
        if (filter.MinPriority is { } minPrio)
        {
            Priority effective = env.Priority ?? Priority.Normal;
            if ((int)effective < (int)minPrio)
            {
                return false;
            }
        }
        return true;
    }

    private Envelope.Envelope BuildBackfillCompleteMarker(SubscriptionRecord record)
    {
        return new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "event.emit",
            Timestamp = _time.GetUtcNow(),
            Payload = new Messages.Telemetry.EventEmit("subscription.backfill_complete"),
            SessionId = record.SubscriberSessionId,
            SubscriptionId = record.Id,
        };
    }
}
