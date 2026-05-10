using ARCP.Envelope;
using ARCP.Ids;

namespace ARCP.Messages.Subscriptions;

/// <summary>§13.2 subscription filter. AND across keys; arrays inside a key are OR'd.</summary>
public sealed record SubscribeFilter
{
    /// <summary>Match these session ids only.</summary>
    public IReadOnlyList<string>? SessionId { get; init; }

    /// <summary>Match these job ids only.</summary>
    public IReadOnlyList<string>? JobId { get; init; }

    /// <summary>Match these stream ids only.</summary>
    public IReadOnlyList<string>? StreamId { get; init; }

    /// <summary>Match these trace ids only.</summary>
    public IReadOnlyList<string>? TraceId { get; init; }

    /// <summary>Match these wire types only.</summary>
    public IReadOnlyList<string>? Types { get; init; }

    /// <summary>Match envelopes whose <see cref="Envelope.Priority" /> is &gt;= this value.</summary>
    public Envelope.Priority? MinPriority { get; init; }
}

/// <summary>§13.3 backfill anchor.</summary>
/// <param name="AfterMessageId">Replay envelopes whose sequence is strictly after this message.</param>
/// <param name="CheckpointId">Reserved for v0.2 (parsed but ignored in v0.1).</param>
public sealed record SubscribeSince(
    string? AfterMessageId = null,
    string? CheckpointId = null);

/// <summary>§13.1 subscribe to a filtered stream of events.</summary>
public sealed record Subscribe(
    SubscribeFilter Filter,
    SubscribeSince? Since = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "subscribe";
}

/// <summary>§13.1 subscription accepted.</summary>
public sealed record SubscribeAccepted(SubscriptionId SubscriptionId) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "subscribe.accepted";
}

/// <summary>§13.1 wrapped event delivery.</summary>
public sealed record SubscribeEvent(System.Text.Json.JsonElement Event) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "subscribe.event";
}

/// <summary>§13.4 client &gt; runtime: terminate a subscription.</summary>
public sealed record Unsubscribe(SubscriptionId SubscriptionId) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "unsubscribe";
}

/// <summary>§13.4 runtime &gt; client: subscription closed.</summary>
public sealed record SubscribeClosed(
    SubscriptionId SubscriptionId,
    string Reason) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "subscribe.closed";
}
