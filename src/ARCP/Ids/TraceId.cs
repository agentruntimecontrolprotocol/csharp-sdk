using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>
/// Distributed trace identifier per RFC-0001-v2 §6.1.1 / §17.1.
/// Compatible with OpenTelemetry-style trace ids.
/// </summary>
/// <param name="Value">The wire-form id string.</param>
[JsonConverter(typeof(StringIdJsonConverter<TraceId>))]
public readonly record struct TraceId(string Value) : IStringId<TraceId>
{
    /// <summary>Generate a fresh trace id.</summary>
    /// <returns>A new <see cref="TraceId" />.</returns>
    public static TraceId New() => new($"trace_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static TraceId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
