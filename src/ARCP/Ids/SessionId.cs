using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>
/// Identifier for an authenticated ARCP session per RFC-0001-v2 §8 / §9.
/// </summary>
/// <param name="Value">The wire-form id string (e.g. <c>sess_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<SessionId>))]
public readonly record struct SessionId(string Value) : IStringId<SessionId>
{
    /// <summary>Generate a fresh, ULID-suffixed session id.</summary>
    /// <returns>A new <see cref="SessionId" />.</returns>
    public static SessionId New() => new($"sess_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static SessionId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
