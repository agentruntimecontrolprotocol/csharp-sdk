// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcp.Core.Wire;

/// <summary>Custom envelope (de)serializer. Reads the <c>type</c> field first to choose the
/// concrete payload type from <see cref="MessageTypeRegistry"/>. Unknown <c>type</c> values produce
/// an envelope with a <see cref="JsonElement"/> payload — they round-trip without loss per spec §5.1.</summary>
public sealed class EnvelopeJsonConverter : JsonConverter<Envelope>
{
    private readonly MessageTypeRegistry _registry;

    public EnvelopeJsonConverter() : this(MessageTypeRegistry.Default) { }

    public EnvelopeJsonConverter(MessageTypeRegistry registry)
    {
        _registry = registry;
    }

    private sealed class EnvelopeFields
    {
        public string? Type;
        public string? Arcp = "1.1";
        public string? Id;
        public string? SessionId;
        public string? TraceId;
        public string? SpanId;
        public string? ParentSpanId;
        public string? JobId;
        public long? EventSeq;
        public DateTimeOffset Timestamp = DateTimeOffset.UtcNow;
        public JsonElement? PayloadElement;
        public Dictionary<string, JsonElement>? Extensions;
    }

    public override Envelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object for ARCP envelope.");

        using var doc = JsonDocument.ParseValue(ref reader);
        var fields = ParseFields(doc.RootElement);
        ValidateHeader(fields);
        var payload = DeserializePayload(fields, options);

        return new Envelope
        {
            Arcp = fields.Arcp ?? "1.1",
            Id = fields.Id ?? "msg_" + Ulid.NewUlid(),
            Type = fields.Type!,
            SessionId = fields.SessionId,
            TraceId = fields.TraceId,
            SpanId = fields.SpanId,
            ParentSpanId = fields.ParentSpanId,
            JobId = fields.JobId,
            EventSeq = fields.EventSeq,
            Timestamp = fields.Timestamp,
            Payload = payload,
            Extensions = fields.Extensions,
        };
    }

    private static EnvelopeFields ParseFields(JsonElement root)
    {
        var fields = new EnvelopeFields();
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "arcp": fields.Arcp = p.Value.GetString(); break;
                case "id": fields.Id = p.Value.GetString(); break;
                case "type": fields.Type = p.Value.GetString(); break;
                case "session_id": fields.SessionId = p.Value.GetString(); break;
                case "trace_id": fields.TraceId = p.Value.GetString(); break;
                case "span_id": fields.SpanId = p.Value.GetString(); break;
                case "parent_span_id": fields.ParentSpanId = p.Value.GetString(); break;
                case "job_id": fields.JobId = p.Value.GetString(); break;
                case "event_seq":
                    if (p.Value.ValueKind == JsonValueKind.Number) fields.EventSeq = p.Value.GetInt64();
                    break;
                case "timestamp":
                    if (p.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.Value.GetString(), out var ts))
                        fields.Timestamp = ts;
                    break;
                case "payload": fields.PayloadElement = p.Value.Clone(); break;
                default:
                    fields.Extensions ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    fields.Extensions[p.Name] = p.Value.Clone();
                    break;
            }
        }
        return fields;
    }

    private static void ValidateHeader(EnvelopeFields fields)
    {
        if (string.IsNullOrEmpty(fields.Type))
            throw new Errors.InvalidRequestException("Envelope missing required 'type' field (spec §5.1).");
        if (fields.Arcp is not null && fields.Arcp != "1.1")
            throw new Errors.InvalidRequestException($"Unsupported ARCP envelope version: '{fields.Arcp}' (spec §5.1; expected '1.1').");
    }

    private object? DeserializePayload(EnvelopeFields fields, JsonSerializerOptions options)
    {
        if (fields.PayloadElement is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } pe)
            return null;
        if (_registry.TryGet(fields.Type!, out var clrType) && clrType is not null)
            return pe.Deserialize(clrType, options);
        return pe;
    }

    public override void Write(Utf8JsonWriter writer, Envelope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteHeader(writer, value);
        WritePayload(writer, value, options);
        WriteExtensions(writer, value);
        writer.WriteEndObject();
    }

    private static void WriteHeader(Utf8JsonWriter writer, Envelope value)
    {
        writer.WriteString("arcp", value.Arcp);
        writer.WriteString("id", value.Id);
        writer.WriteString("type", value.Type);
        if (value.SessionId is not null) writer.WriteString("session_id", value.SessionId);
        if (value.TraceId is not null) writer.WriteString("trace_id", value.TraceId);
        if (value.SpanId is not null) writer.WriteString("span_id", value.SpanId);
        if (value.ParentSpanId is not null) writer.WriteString("parent_span_id", value.ParentSpanId);
        if (value.JobId is not null) writer.WriteString("job_id", value.JobId);
        if (value.EventSeq is { } seq) writer.WriteNumber("event_seq", seq);
        writer.WriteString("timestamp", value.Timestamp);
    }

    private static void WritePayload(Utf8JsonWriter writer, Envelope value, JsonSerializerOptions options)
    {
        if (value.Payload is null) return;
        writer.WritePropertyName("payload");
        if (value.Payload is JsonElement el)
        {
            el.WriteTo(writer);
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);
        }
    }

    private static void WriteExtensions(Utf8JsonWriter writer, Envelope value)
    {
        if (value.Extensions is null) return;
        foreach (var kv in value.Extensions)
        {
            writer.WritePropertyName(kv.Key);
            kv.Value.WriteTo(writer);
        }
    }
}
