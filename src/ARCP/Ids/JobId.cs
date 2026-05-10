using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>Identifier for a durable job per RFC-0001-v2 §10.</summary>
/// <param name="Value">The wire-form id string (e.g. <c>job_01J...</c>).</param>
[JsonConverter(typeof(StringIdJsonConverter<JobId>))]
public readonly record struct JobId(string Value) : IStringId<JobId>
{
    /// <summary>Generate a fresh, ULID-suffixed job id.</summary>
    /// <returns>A new <see cref="JobId" />.</returns>
    public static JobId New() => new($"job_{Ulid.NewUlid()}");

    /// <inheritdoc />
    public static JobId FromString(string value)
        => new(IdValidation.EnsureNotEmpty(value, nameof(value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
