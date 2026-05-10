using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>Identifier for an artifact per RFC-0001-v2 §16.</summary>
/// <param name="Value">The wire-form id string (e.g. <c>art_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<ArtifactId>))]
public readonly record struct ArtifactId(string Value) : IStringId<ArtifactId>
{
    /// <summary>Generate a fresh, ULID-suffixed artifact id.</summary>
    /// <returns>A new <see cref="ArtifactId" />.</returns>
    public static ArtifactId New() => new($"art_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static ArtifactId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
