using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>Identifier for a permission lease per RFC-0001-v2 §15.5.</summary>
/// <param name="Value">The wire-form id string (e.g. <c>lease_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<LeaseId>))]
public readonly record struct LeaseId(string Value) : IStringId<LeaseId>
{
    /// <summary>Generate a fresh, ULID-suffixed lease id.</summary>
    /// <returns>A new <see cref="LeaseId" />.</returns>
    public static LeaseId New() => new($"lease_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static LeaseId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
