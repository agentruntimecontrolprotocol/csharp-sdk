using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>
/// Identifier for a single envelope message per RFC-0001-v2 §6.1.1
/// (transport idempotency key).
/// </summary>
/// <param name="Value">The wire-form id string (e.g. <c>msg_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<MessageId>))]
public readonly record struct MessageId(string Value) : IStringId<MessageId>
{
    /// <summary>Generate a fresh, ULID-suffixed message id.</summary>
    /// <returns>A new <see cref="MessageId" />.</returns>
    public static MessageId New() => new($"msg_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static MessageId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
