// SPDX-License-Identifier: Apache-2.0
using System;
using System.Diagnostics.CodeAnalysis;

namespace Arcp.Core.Agents;

/// <summary>A reference to an agent: <c>name</c> or <c>name@version</c> (spec §7.5).</summary>
public readonly record struct AgentRef : IParsable<AgentRef>
{
    /// <summary>Agent name. Grammar: <c>[a-z0-9][a-z0-9._-]*</c>.</summary>
    public string Name { get; }

    /// <summary>Pinned version, or null when the agent reference is unversioned (resolves to default).</summary>
    public string? Version { get; }

    /// <summary>Initializes a new instance of the <see cref="AgentRef"/> class.</summary>
    public AgentRef(string name, string? version = null)
    {
        if (string.IsNullOrEmpty(name) || !IsValidName(name))
            throw new ArgumentException($"Invalid agent name: '{name}'", nameof(name));
        if (version is not null && !IsValidVersion(version))
            throw new ArgumentException($"Invalid agent version: '{version}'", nameof(version));
        Name = name;
        Version = version;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Version is null ? Name : $"{Name}@{Version}";

    /// <summary>Parses a string into an instance, throwing on invalid input.</summary>
    public static AgentRef Parse(string s, IFormatProvider? provider = null)
    {
        if (TryParse(s, provider, out var r)) return r;
        throw new FormatException($"Invalid agent reference: '{s}'");
    }

    /// <summary>Attempts to parse a string into an instance, returning <c>false</c> on failure.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AgentRef result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;
        var at = s.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            if (!IsValidName(s)) return false;
            result = new AgentRef(s);
            return true;
        }
        var name = s[..at];
        var version = s[(at + 1)..];
        if (!IsValidName(name) || !IsValidVersion(version)) return false;
        result = new AgentRef(name, version);
        return true;
    }

    internal static bool IsValidName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var first = s[0];
        if (!(char.IsAsciiLetterLower(first) || char.IsAsciiDigit(first))) return false;
        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '.' || c == '_' || c == '-'))
                return false;
        }
        return true;
    }

    internal static bool IsValidVersion(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '.' || c == '+' || c == '_' || c == '-'))
                return false;
        }
        return true;
    }
}
