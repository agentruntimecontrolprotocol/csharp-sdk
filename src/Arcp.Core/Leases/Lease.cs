// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace Arcp.Core.Leases;

/// <summary>A capability lease (spec §9). A map from reserved capability namespace to a list of
/// pattern strings (globs, URL patterns, budget amount strings, etc.). Serializes as a flat object
/// of <c>{ "fs.read": ["..."], "cost.budget": ["USD:5.00"] }</c>.</summary>
[JsonConverter(typeof(LeaseJsonConverter))]
public sealed record Lease
{
    /// <summary>Gets the capabilities.</summary>
    public IDictionary<string, IReadOnlyList<string>> Capabilities { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="Lease"/> class.</summary>
    public Lease() { }

    /// <summary>Initializes a new instance of the <see cref="Lease"/> class.</summary>
    public Lease(IReadOnlyDictionary<string, IReadOnlyList<string>> capabilities)
    {
        Capabilities = capabilities.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>Get.</summary>
    public IReadOnlyList<string> Get(string namespaceName) =>
        Capabilities.TryGetValue(namespaceName, out var v) ? v : Array.Empty<string>();
}

internal sealed class LeaseJsonConverter : System.Text.Json.Serialization.JsonConverter<Lease>
{
    public override Lease Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            throw new System.Text.Json.JsonException("Expected start of lease object.");
        var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        using var doc = System.Text.Json.JsonDocument.ParseValue(ref reader);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            var list = new List<string>();
            foreach (var el in p.Value.EnumerateArray())
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.String && el.GetString() is { } s) list.Add(s);
            }
            dict[p.Name] = list.AsReadOnly();
        }
        return new Lease(dict);
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, Lease value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kv in value.Capabilities)
        {
            writer.WritePropertyName(kv.Key);
            writer.WriteStartArray();
            foreach (var s in kv.Value) writer.WriteStringValue(s);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}

/// <summary>Reserved lease capability namespaces (spec §9.2).</summary>
public static class LeaseNamespaces
{
    /// <summary>Gets the fs read.</summary>
    public const string FsRead = "fs.read";
    /// <summary>Gets the fs write.</summary>
    public const string FsWrite = "fs.write";
    /// <summary>Gets the net fetch.</summary>
    public const string NetFetch = "net.fetch";
    /// <summary>Gets the tool call.</summary>
    public const string ToolCall = "tool.call";
    /// <summary>Gets the agent delegate.</summary>
    public const string AgentDelegate = "agent.delegate";
    /// <summary>Gets the cost budget.</summary>
    public const string CostBudget = "cost.budget";
    /// <summary>Gets the model use.</summary>
    public const string ModelUse = "model.use";

    /// <summary>Gets the all.</summary>
    public static readonly FrozenSet<string> All = new HashSet<string>
    {
        FsRead, FsWrite, NetFetch, ToolCall, AgentDelegate, CostBudget, ModelUse,
    }.ToFrozenSet();
}

/// <summary>Lease constraints carried on <c>job.submit</c> and echoed on <c>job.accepted</c> (spec §9.5).</summary>
public sealed record LeaseConstraints
{
    /// <summary>ISO 8601 UTC expiration instant. MUST be UTC (<c>Z</c>), MUST be in the future at
    /// submission time. <see langword="null"/> means the lease never expires.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>A parsed <c>cost.budget</c> amount string of the form <c>currency:decimal</c> (spec §9.6).</summary>
public readonly record struct BudgetAmount(string Currency, decimal Amount)
{
    /// <summary>Returns the string representation.</summary>
    public override string ToString() =>
        $"{Currency}:{Amount.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses a string into an instance, throwing on invalid input.</summary>
    public static BudgetAmount Parse(string s)
    {
        if (TryParse(s, out var v)) return v;
        throw new FormatException($"Invalid budget amount: '{s}'");
    }

    /// <summary>Attempts to parse a string into an instance, returning <c>false</c> on failure.</summary>
    public static bool TryParse(string? s, out BudgetAmount value)
    {
        value = default;
        if (string.IsNullOrEmpty(s)) return false;
        var colon = s.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == s.Length - 1) return false;
        var currency = s[..colon];
        var amountStr = s[(colon + 1)..];
        foreach (var c in currency)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-')) return false;
        }
        if (!decimal.TryParse(amountStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            return false;
        if (amount < 0) return false;
        value = new BudgetAmount(currency, amount);
        return true;
    }
}
