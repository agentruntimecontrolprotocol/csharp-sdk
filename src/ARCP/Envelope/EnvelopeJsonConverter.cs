using System.Text.Json;
using System.Text.Json.Serialization;
using ARCP.Errors;
using ARCP.Extensions;
using ARCP.Ids;

namespace ARCP.Envelope;

/// <summary>
/// Custom <see cref="JsonConverter{T}" /> for <see cref="Envelope" /> that
/// hoists the wire <c>type</c> discriminator to the envelope level (per
/// RFC-0001-v2 §6.1.1) rather than nesting it inside <c>payload</c>.
/// </summary>
/// <remarks>
/// <para>
/// The runtime registers this converter in <see cref="EnvelopeJson.Options" />
/// and resolves the payload's CLR type via a
/// <see cref="MessageTypeRegistry" /> supplied to the converter constructor.
/// </para>
/// <para>
/// On read: parses to a <see cref="JsonDocument" />, reads <c>type</c>,
/// resolves the CLR payload type from the registry, deserializes the
/// <c>payload</c> sub-object as that concrete type, and assembles the
/// envelope.
/// </para>
/// <para>
/// On write: emits envelope-level fields and writes <c>payload</c> as the
/// concrete payload type so payload-specific converters apply, then closes
/// the object.
/// </para>
/// </remarks>
public sealed class EnvelopeJsonConverter : JsonConverter<Envelope>
{
    private readonly MessageTypeRegistry _registry;
    private readonly ExtensionRegistry? _extensions;

    /// <summary>Initializes a converter against the default core catalog.</summary>
    public EnvelopeJsonConverter()
        : this(MessageTypeRegistry.CoreCatalog(), null)
    {
    }

    /// <summary>Initializes a converter against the provided registries.</summary>
    /// <param name="registry">Core message-type registry.</param>
    /// <param name="extensions">Optional extension registry for namespaced types.</param>
    public EnvelopeJsonConverter(MessageTypeRegistry registry, ExtensionRegistry? extensions)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _extensions = extensions;
    }

    /// <inheritdoc />
    public override Envelope Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                $"Envelope: expected StartObject, got {reader.TokenType}.");
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Envelope: expected JSON object.");
        }

        string type = ReadRequiredString(root, "type");
        string arcp = ReadRequiredString(root, "arcp");
        string idStr = ReadRequiredString(root, "id");
        string tsStr = ReadRequiredString(root, "timestamp");

        DateTimeOffset timestamp;
        try
        {
            timestamp = DateTimeOffset.Parse(tsStr,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
        }
        catch (FormatException ex)
        {
            throw new JsonException(
                $"Envelope.timestamp: invalid RFC 3339 / ISO 8601 timestamp \"{tsStr}\".",
                ex);
        }

        Type? payloadType = _registry.Resolve(type);
        MessageType payload;
        if (payloadType is not null)
        {
            JsonElement payloadElem = TryGetProperty(root, "payload");
            payload = (MessageType?)payloadElem.Deserialize(payloadType, options)
                ?? throw new JsonException($"Envelope.payload: deserialized to null for type \"{type}\".");
        }
        else if (_extensions is not null && _extensions.Has(type))
        {
            JsonElement payloadElem = TryGetProperty(root, "payload");
            object? raw = _extensions.Parse(type, payloadElem, options);
            if (raw is not MessageType mt)
            {
                throw new InvalidArgumentException(
                    $"Extension \"{type}\" payload type does not derive from MessageType.");
            }
            payload = mt;
        }
        else
        {
            throw new UnimplementedException(
                "§21.3",
                $"Unknown envelope type \"{type}\". Register it via MessageTypeRegistry or ExtensionRegistry.");
        }

        return new Envelope
        {
            Arcp = arcp,
            Id = MessageId.FromString(idStr),
            Type = type,
            Timestamp = timestamp,
            Payload = payload,
            Source = ReadOptionalString(root, "source"),
            Target = ReadOptionalString(root, "target"),
            SessionId = ReadOptionalId(root, "session_id", SessionId.FromString),
            JobId = ReadOptionalId(root, "job_id", JobId.FromString),
            StreamId = ReadOptionalId(root, "stream_id", StreamId.FromString),
            SubscriptionId = ReadOptionalId(root, "subscription_id", SubscriptionId.FromString),
            TraceId = ReadOptionalId(root, "trace_id", TraceId.FromString),
            SpanId = ReadOptionalId(root, "span_id", SpanId.FromString),
            ParentSpanId = ReadOptionalId(root, "parent_span_id", SpanId.FromString),
            CorrelationId = ReadOptionalId(root, "correlation_id", MessageId.FromString),
            CausationId = ReadOptionalId(root, "causation_id", MessageId.FromString),
            IdempotencyKey = ReadOptionalId(root, "idempotency_key", IdempotencyKey.FromString),
            Priority = ReadOptionalEnum(root, "priority", options),
            Extensions = ReadOptionalExtensions(root),
        };
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        Envelope value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        writer.WriteStartObject();

        writer.WriteString("arcp", value.Arcp);
        writer.WriteString("id", value.Id.Value);
        writer.WriteString("type", value.Type);
        writer.WriteString("timestamp", FormatTimestamp(value.Timestamp));

        WriteOptionalString(writer, "source", value.Source);
        WriteOptionalString(writer, "target", value.Target);
        WriteOptionalId(writer, "session_id", value.SessionId);
        WriteOptionalId(writer, "job_id", value.JobId);
        WriteOptionalId(writer, "stream_id", value.StreamId);
        WriteOptionalId(writer, "subscription_id", value.SubscriptionId);
        WriteOptionalId(writer, "trace_id", value.TraceId);
        WriteOptionalId(writer, "span_id", value.SpanId);
        WriteOptionalId(writer, "parent_span_id", value.ParentSpanId);
        WriteOptionalId(writer, "correlation_id", value.CorrelationId);
        WriteOptionalId(writer, "causation_id", value.CausationId);
        WriteOptionalId(writer, "idempotency_key", value.IdempotencyKey);

        if (value.Priority is { } p)
        {
            writer.WritePropertyName("priority");
            JsonSerializer.Serialize(writer, p, options);
        }

        if (value.Extensions is { Count: > 0 } ext)
        {
            writer.WritePropertyName("extensions");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, JsonElement> kv in ext)
            {
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Format a <see cref="DateTimeOffset" /> in RFC 3339 with millisecond
    /// precision, emitting <c>Z</c> for UTC and an explicit numeric offset
    /// otherwise. Matches the example envelope wire format from §6.1.1.
    /// </summary>
    private static string FormatTimestamp(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return utc.ToString(
            "yyyy-MM-ddTHH:mm:ss.fff",
            System.Globalization.CultureInfo.InvariantCulture) + "Z";
    }

    private static string ReadRequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Envelope.{name} is required and must be a string.");
        }
        string? s = v.GetString();
        if (string.IsNullOrEmpty(s))
        {
            throw new JsonException($"Envelope.{name} must be non-empty.");
        }
        return s;
    }

    private static string? ReadOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static T? ReadOptionalId<T>(JsonElement root, string name, Func<string, T> factory)
        where T : struct
    {
        if (!root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        string? s = v.GetString();
        return string.IsNullOrEmpty(s) ? null : factory(s);
    }

    private static Priority? ReadOptionalEnum(JsonElement root, string name, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return v.Deserialize<Priority>(options);
    }

    private static Dictionary<string, JsonElement>? ReadOptionalExtensions(JsonElement root)
    {
        if (!root.TryGetProperty("extensions", out JsonElement v) || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        Dictionary<string, JsonElement> dict = new(StringComparer.Ordinal);
        foreach (JsonProperty prop in v.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }
        return dict;
    }

    private static JsonElement TryGetProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement v))
        {
            // Allow envelopes whose payload schema is the empty object (e.g. ping):
            // synthesize an empty object so deserialization can produce a default payload.
            using JsonDocument empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
        return v;
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteOptionalId<T>(Utf8JsonWriter writer, string name, T? id)
        where T : struct, IStringId<T>
    {
        if (id is { } v)
        {
            writer.WriteString(name, v.Value);
        }
    }
}

/// <summary>
/// Default <see cref="JsonSerializerOptions" /> used by ARCP. Configured to
/// emit a compact, RFC-3339-friendly wire format, with snake-case property
/// names and a single shared <see cref="EnvelopeJsonConverter" /> instance.
/// </summary>
public static class EnvelopeJson
{
    /// <summary>The default serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions(null);

    /// <summary>
    /// Build options for a specific <see cref="MessageTypeRegistry" /> /
    /// <see cref="ExtensionRegistry" /> combination — used by the runtime once
    /// the Phase 2 registry exists.
    /// </summary>
    /// <param name="registry">Core registry, or <see langword="null" /> to use the core catalog.</param>
    /// <param name="extensions">Optional extension registry.</param>
    /// <returns>Configured options.</returns>
    public static JsonSerializerOptions CreateOptions(
        MessageTypeRegistry? registry,
        ExtensionRegistry? extensions = null)
    {
        JsonSerializerOptions opts = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false,
        };
        opts.Converters.Add(new EnvelopeJsonConverter(
            registry ?? MessageTypeRegistry.CoreCatalog(),
            extensions));
        return opts;
    }
}
