using System.Text.Json;
using System.Text.Json.Serialization;

namespace ARCP.Ids;

/// <summary>
/// Generic <see cref="JsonConverter{T}" /> that reads and writes a newtype
/// identifier as a bare JSON string. Without it, <c>readonly record struct
/// SessionId(string Value)</c> would default-serialize as
/// <c>{ "value": "sess_..." }</c> — undesirable per RFC-0001-v2 §6.1.1.
/// </summary>
/// <typeparam name="T">The id record struct.</typeparam>
public sealed class StringIdJsonConverter<T> : JsonConverter<T>
    where T : struct, IStringId<T>
{
    /// <inheritdoc />
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected string for {typeof(T).Name}, got {reader.TokenType}.");
        }

        string? value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            throw new JsonException($"{typeof(T).Name} must be a non-empty string.");
        }

        return T.FromString(value);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
