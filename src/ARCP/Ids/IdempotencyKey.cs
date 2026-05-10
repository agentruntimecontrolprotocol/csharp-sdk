using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>
/// Logical idempotency key per RFC-0001-v2 §6.4. Carried on the
/// <see cref="ARCP.Envelope.Envelope" /> as the user-supplied dedup key for
/// retried command intent. Unlike the other ids in this namespace,
/// <see cref="IdempotencyKey" /> is never auto-generated; callers supply it.
/// </summary>
/// <param name="Value">The user-supplied key string.</param>
[JsonConverter(typeof(StringIdJsonConverter<IdempotencyKey>))]
public readonly record struct IdempotencyKey(string Value) : IStringId<IdempotencyKey>
{
    /// <inheritdoc />
    public static IdempotencyKey FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
