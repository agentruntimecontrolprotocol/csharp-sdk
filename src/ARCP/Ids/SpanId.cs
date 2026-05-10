using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>Trace span identifier per RFC-0001-v2 §17.1.</summary>
/// <param name="Value">The wire-form id string.</param>
[JsonConverter(typeof(StringIdJsonConverter<SpanId>))]
public readonly record struct SpanId(string Value) : IStringId<SpanId>
{
    /// <summary>Generate a fresh span id.</summary>
    /// <returns>A new <see cref="SpanId" />.</returns>
    public static SpanId New() => new($"span_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static SpanId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
