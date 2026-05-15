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

    public override Envelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object for ARCP envelope.");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        string? type = null;
        string? arcp = "1";
        string? id = null;
        string? sessionId = null;
        string? traceId = null;
        string? spanId = null;
        string? parentSpanId = null;
        string? jobId = null;
        long? eventSeq = null;
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        JsonElement? payloadElement = null;
        Dictionary<string, JsonElement>? extensions = null;

        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "arcp": arcp = p.Value.GetString(); break;
                case "id": id = p.Value.GetString(); break;
                case "type": type = p.Value.GetString(); break;
                case "session_id": sessionId = p.Value.GetString(); break;
                case "trace_id": traceId = p.Value.GetString(); break;
                case "span_id": spanId = p.Value.GetString(); break;
                case "parent_span_id": parentSpanId = p.Value.GetString(); break;
                case "job_id": jobId = p.Value.GetString(); break;
                case "event_seq":
                    if (p.Value.ValueKind == JsonValueKind.Number) eventSeq = p.Value.GetInt64();
                    break;
                case "timestamp":
                    if (p.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.Value.GetString(), out var ts))
                        timestamp = ts;
                    break;
                case "payload": payloadElement = p.Value.Clone(); break;
                default:
                    extensions ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    extensions[p.Name] = p.Value.Clone();
                    break;
            }
        }

        if (string.IsNullOrEmpty(type))
            throw new Errors.InvalidRequestException("Envelope missing required 'type' field (spec §5.1).");
        if (arcp is not null && arcp != "1")
            throw new Errors.InvalidRequestException($"Unsupported ARCP envelope version: '{arcp}' (spec §5.1; expected '1').");

        object? payload = null;
        if (payloadElement is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } pe)
        {
            if (_registry.TryGet(type, out var clrType) && clrType is not null)
            {
                payload = pe.Deserialize(clrType, options);
            }
            else
            {
                payload = pe;
            }
        }

        return new Envelope
        {
            Arcp = arcp ?? "1",
            Id = id ?? "msg_" + Ulid.NewUlid(),
            Type = type,
            SessionId = sessionId,
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            JobId = jobId,
            EventSeq = eventSeq,
            Timestamp = timestamp,
            Payload = payload,
            Extensions = extensions,
        };
    }

    public override void Write(Utf8JsonWriter writer, Envelope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
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

        if (value.Payload is not null)
        {
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

        if (value.Extensions is not null)
        {
            foreach (var kv in value.Extensions)
            {
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }
}
