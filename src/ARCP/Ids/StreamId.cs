using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>Identifier for a stream per RFC-0001-v2 §11.</summary>
/// <param name="Value">The wire-form id string (e.g. <c>str_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<StreamId>))]
public readonly record struct StreamId(string Value) : IStringId<StreamId>
{
    /// <summary>Generate a fresh, ULID-suffixed stream id.</summary>
    /// <returns>A new <see cref="StreamId" />.</returns>
    public static StreamId New() => new($"str_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static StreamId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
