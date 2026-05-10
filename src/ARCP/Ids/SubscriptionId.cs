using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>Identifier for a subscription per RFC-0001-v2 §13.</summary>
/// <param name="Value">The wire-form id string (e.g. <c>sub_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<SubscriptionId>))]
public readonly record struct SubscriptionId(string Value) : IStringId<SubscriptionId>
{
    /// <summary>Generate a fresh, ULID-suffixed subscription id.</summary>
    /// <returns>A new <see cref="SubscriptionId" />.</returns>
    public static SubscriptionId New() => new($"sub_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static SubscriptionId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
