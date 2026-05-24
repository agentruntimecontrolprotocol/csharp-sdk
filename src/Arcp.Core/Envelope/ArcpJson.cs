// SPDX-License-Identifier: Apache-2.0
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcp.Core.Wire;

/// <summary>Canonical <see cref="JsonSerializerOptions"/> for ARCP wire serialization.</summary>
public static class ArcpJson
{
    /// <summary>Gets the options.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>Create.</summary>
    public static JsonSerializerOptions Create(MessageTypeRegistry? registry = null)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        o.Converters.Add(new EnvelopeJsonConverter(registry ?? MessageTypeRegistry.Default));
        return o;
    }

    /// <summary>Serialize.</summary>
    public static string Serialize(Envelope env) => JsonSerializer.Serialize(env, Options);

    /// <summary>Deserialize.</summary>
    public static Envelope Deserialize(string json)
    {
        var env = JsonSerializer.Deserialize<Envelope>(json, Options);
        return env ?? throw new Errors.InvalidRequestException("Could not deserialize ARCP envelope.");
    }

    /// <summary>Deserialize.</summary>
    public static Envelope Deserialize(ReadOnlySpan<byte> utf8)
    {
        var env = JsonSerializer.Deserialize<Envelope>(utf8, Options);
        return env ?? throw new Errors.InvalidRequestException("Could not deserialize ARCP envelope.");
    }

    /// <summary>Serialize utf8.</summary>
    public static byte[] SerializeUtf8(Envelope env) => JsonSerializer.SerializeToUtf8Bytes(env, Options);

    /// <summary>Serialize a value into a <see cref="JsonElement"/> using ARCP wire options.</summary>
    public static JsonElement ToJsonElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
