using System.Text.Json;
using System.Text.Json.Serialization;
using ARCP.Ids;

namespace ARCP.Envelope;

/// <summary>
/// Canonical ARCP message envelope per RFC-0001-v2 §6.1.1. Always serialized
/// and deserialized through <see cref="EnvelopeJsonConverter" /> so the
/// payload polymorphism resolves against the registered
/// <see cref="MessageTypeRegistry" />.
/// </summary>
/// <remarks>
/// Construct envelopes via <see cref="Builder.Build{TPayload}" /> in tests and
/// runtime code; direct property-initializer use also works because every
/// required field has the <c>required</c> modifier.
/// </remarks>
[JsonConverter(typeof(EnvelopeJsonConverter))]
public sealed record Envelope
{
    /// <summary>Protocol version understood by the sender (§6.1.1).</summary>
    public required string Arcp { get; init; }

    /// <summary>Globally unique message id; transport-level idempotency key.</summary>
    public required MessageId Id { get; init; }

    /// <summary>Wire-form discriminator for <see cref="Payload" />.</summary>
    public required string Type { get; init; }

    /// <summary>Sender timestamp in RFC 3339 / ISO 8601 format (UTC recommended).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Type-specific body validated by the message schema.</summary>
    public required MessageType Payload { get; init; }

    /// <summary>Logical sender id, such as client, runtime, or agent name.</summary>
    public string? Source { get; init; }

    /// <summary>Logical recipient id, such as runtime, tool host, or agent name.</summary>
    public string? Target { get; init; }

    /// <summary>Required once a session exists.</summary>
    public SessionId? SessionId { get; init; }

    /// <summary>Required for durable job events.</summary>
    public JobId? JobId { get; init; }

    /// <summary>Required for stream events.</summary>
    public StreamId? StreamId { get; init; }

    /// <summary>Required for subscription delivery.</summary>
    public SubscriptionId? SubscriptionId { get; init; }

    /// <summary>Stable id for one user-visible request or workflow.</summary>
    public TraceId? TraceId { get; init; }

    /// <summary>Span id for the current operation.</summary>
    public SpanId? SpanId { get; init; }

    /// <summary>Parent span id when this message is part of a trace tree.</summary>
    public SpanId? ParentSpanId { get; init; }

    /// <summary>Id of the command or request this message answers.</summary>
    public MessageId? CorrelationId { get; init; }

    /// <summary>Id of the message that directly caused this message.</summary>
    public MessageId? CausationId { get; init; }

    /// <summary>
    /// Logical idempotency key for the <em>command intent</em>, distinct from
    /// <see cref="Id" />. See §6.4.
    /// </summary>
    public IdempotencyKey? IdempotencyKey { get; init; }

    /// <summary>One of <c>low</c>, <c>normal</c>, <c>high</c>, <c>critical</c>. Default <c>normal</c>. See §6.5.</summary>
    public Priority? Priority { get; init; }

    /// <summary>Object of namespaced extension fields per §21.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>Convenience builders for envelopes; usable from tests and runtime code.</summary>
    public static class Builder
    {
        /// <summary>
        /// Construct an envelope with sane defaults
        /// (<see cref="ProtocolVersion.Wire" />, fresh <see cref="MessageId" />,
        /// <c>UtcNow</c> timestamp). The wire <c>type</c> is taken from
        /// <see cref="MessageType.WireType" />.
        /// </summary>
        /// <typeparam name="TPayload">The payload type.</typeparam>
        /// <param name="payload">The payload instance.</param>
        /// <param name="timestamp">Optional override for <see cref="Envelope.Timestamp" />.</param>
        /// <param name="id">Optional override for <see cref="Envelope.Id" />.</param>
        /// <returns>A fresh envelope.</returns>
        public static Envelope Build<TPayload>(
            TPayload payload,
            DateTimeOffset? timestamp = null,
            MessageId? id = null)
            where TPayload : MessageType
        {
            ArgumentNullException.ThrowIfNull(payload);
            return new Envelope
            {
                Arcp = ProtocolVersion.Wire,
                Id = id ?? MessageId.New(),
                Type = payload.WireType,
                Timestamp = timestamp ?? DateTimeOffset.UtcNow,
                Payload = payload,
            };
        }
    }
}
