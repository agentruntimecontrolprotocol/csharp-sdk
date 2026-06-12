// SPDX-License-Identifier: Apache-2.0
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Arcp.Core.Ids;

/// <summary>A ULID-based message identifier (envelope <c>id</c>, spec §5).</summary>
public readonly record struct MessageId(string Value) : IParsable<MessageId>
{
    /// <summary>New.</summary>
    public static MessageId New() => new("msg_" + Ulid.NewUlid().ToString());

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;

    /// <summary>Parses a string into an instance, throwing on invalid input.</summary>
    public static MessageId Parse(string s, IFormatProvider? provider = null) => new(s ?? throw new ArgumentNullException(nameof(s)));

    /// <summary>Attempts to parse a string into an instance, returning <c>false</c> on failure.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out MessageId result)
    {
        if (string.IsNullOrEmpty(s)) { result = default; return false; }
        result = new MessageId(s);
        return true;
    }
}

/// <summary>A session identifier minted by the runtime on <c>session.welcome</c> (spec §6.2).</summary>
public readonly record struct SessionId(string Value) : IParsable<SessionId>
{
    /// <summary>New.</summary>
    public static SessionId New() => new("sess_" + Ulid.NewUlid().ToString());

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;

    /// <summary>Parses a string into an instance, throwing on invalid input.</summary>
    public static SessionId Parse(string s, IFormatProvider? provider = null) => new(s ?? throw new ArgumentNullException(nameof(s)));

    /// <summary>Attempts to parse a string into an instance, returning <c>false</c> on failure.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SessionId result)
    {
        if (string.IsNullOrEmpty(s)) { result = default; return false; }
        result = new SessionId(s);
        return true;
    }
}

/// <summary>A job identifier minted by the runtime on <c>job.accepted</c> (spec §7.1).</summary>
public readonly record struct JobId(string Value) : IParsable<JobId>
{
    /// <summary>New.</summary>
    public static JobId New() => new("job_" + Ulid.NewUlid().ToString());

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;

    /// <summary>Parses a string into an instance, throwing on invalid input.</summary>
    public static JobId Parse(string s, IFormatProvider? provider = null) => new(s ?? throw new ArgumentNullException(nameof(s)));

    /// <summary>Attempts to parse a string into an instance, returning <c>false</c> on failure.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out JobId result)
    {
        if (string.IsNullOrEmpty(s)) { result = default; return false; }
        result = new JobId(s);
        return true;
    }
}

/// <summary>A W3C trace context trace identifier (32 hex chars, spec §11).</summary>
public readonly record struct TraceId(string Value) : IParsable<TraceId>
{
    /// <summary>New.</summary>
    public static TraceId New() => new(Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture));

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;

    /// <summary>Parses a string into an instance, throwing on invalid input.</summary>
    public static TraceId Parse(string s, IFormatProvider? provider = null) => new(s ?? throw new ArgumentNullException(nameof(s)));

    /// <summary>Attempts to parse a string into an instance, returning <c>false</c> on failure.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TraceId result)
    {
        if (string.IsNullOrEmpty(s)) { result = default; return false; }
        result = new TraceId(s);
        return true;
    }
}

/// <summary>A W3C trace context span identifier (16 hex chars, spec §11).</summary>
public readonly record struct SpanId(string Value)
{
    /// <summary>New.</summary>
    public static SpanId New()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return new SpanId(Convert.ToHexString(bytes).ToLowerInvariant());
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
}

/// <summary>The identifier for a streamed-result group (spec §8.4).</summary>
public readonly record struct ResultId(string Value)
{
    /// <summary>New.</summary>
    public static ResultId New() => new("res_" + Ulid.NewUlid().ToString());

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
}

/// <summary>An identifier for an artifact emitted via <c>artifact_ref</c> events (spec §8.2).</summary>
public readonly record struct ArtifactId(string Value)
{
    /// <summary>New.</summary>
    public static ArtifactId New() => new("art_" + Ulid.NewUlid().ToString());

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
}
