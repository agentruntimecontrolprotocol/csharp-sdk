using System.Text.Json.Serialization;

namespace ARCP.Messages.Session;

/// <summary>
/// Authentication scheme advertised by the client per RFC-0001-v2 §8.2.
/// </summary>
/// <remarks>
/// v0.1 supports <see cref="Bearer" />, <see cref="SignedJwt" /> and
/// <see cref="None" /> only. <see cref="Mtls" /> and <see cref="OAuth2" /> are
/// reserved in the wire schema (so we round-trip messages from peers that
/// advertise them) but the runtime rejects them with
/// <see cref="ARCP.Errors.UnimplementedException" /> per PLAN.md §4.1.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AuthScheme>))]
public enum AuthScheme
{
    /// <summary>Opaque token; runtime validates against its trust store.</summary>
    [JsonStringEnumMemberName("bearer")]
    Bearer,

    /// <summary>Mutual TLS already established at the transport.</summary>
    [JsonStringEnumMemberName("mtls")]
    Mtls,

    /// <summary>OAuth 2.0 access token; runtime MAY introspect.</summary>
    [JsonStringEnumMemberName("oauth2")]
    OAuth2,

    /// <summary>Signed JWT with <c>aud</c> equal to the runtime identity.</summary>
    [JsonStringEnumMemberName("signed_jwt")]
    SignedJwt,

    /// <summary>Anonymous; only valid when <c>capabilities.anonymous: true</c>.</summary>
    [JsonStringEnumMemberName("none")]
    None,
}

/// <summary>§8.2 client credential block carried on <c>session.open</c>.</summary>
/// <param name="Scheme">Authentication scheme.</param>
/// <param name="Token">Opaque token (omitted for <see cref="AuthScheme.Mtls" /> and <see cref="AuthScheme.None" />).</param>
public sealed record AuthCredential(
    AuthScheme Scheme,
    string? Token = null);

/// <summary>§8.3 trust classification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrustLevel>))]
public enum TrustLevel
{
    /// <summary>External / public.</summary>
    [JsonStringEnumMemberName("untrusted")]
    Untrusted,

    /// <summary>Limited access.</summary>
    [JsonStringEnumMemberName("constrained")]
    Constrained,

    /// <summary>Internal.</summary>
    [JsonStringEnumMemberName("trusted")]
    Trusted,

    /// <summary>System-level.</summary>
    [JsonStringEnumMemberName("privileged")]
    Privileged,
}
