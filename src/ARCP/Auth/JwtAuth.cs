using ARCP.Errors;
using ARCP.Messages.Session;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ARCP.Auth;

/// <summary>
/// Options for <see cref="JwtAuth" />.
/// </summary>
public sealed class JwtAuthOptions
{
    /// <summary>The expected <c>aud</c> claim per §8.2.</summary>
    public required string Audience { get; init; }

    /// <summary>The expected <c>iss</c> claim, if any.</summary>
    public string? Issuer { get; init; }

    /// <summary>Signing keys to validate against (any-of).</summary>
    public required IReadOnlyList<SecurityKey> SigningKeys { get; init; }

    /// <summary>Allowable clock skew when validating <c>exp</c>/<c>nbf</c>.</summary>
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// JWT verifier per RFC-0001-v2 §8.2. Uses <c>Microsoft.IdentityModel.JsonWebTokens</c>
/// to parse and validate signed JWTs; surfaces the JWT <c>sub</c> claim as
/// the principal in the resulting <see cref="AuthIdentity" />.
/// </summary>
public sealed class JwtAuth : IAuthVerifier
{
    private readonly JwtAuthOptions _options;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Initializes a verifier with the given options.</summary>
    /// <param name="options">The configured options.</param>
    public JwtAuth(JwtAuthOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public AuthScheme Scheme => AuthScheme.SignedJwt;

    /// <inheritdoc />
    public async Task<AuthIdentity> VerifyAsync(
        AuthCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.Scheme != AuthScheme.SignedJwt)
        {
            throw new UnauthenticatedException($"JwtAuth cannot verify scheme {credential.Scheme}.");
        }
        if (string.IsNullOrEmpty(credential.Token))
        {
            throw new UnauthenticatedException("Signed-JWT auth requires a non-empty token.");
        }

        var parameters = new TokenValidationParameters
        {
            ValidAudience = _options.Audience,
            ValidIssuer = _options.Issuer,
            ValidateIssuer = _options.Issuer is not null,
            ValidateAudience = true,
            IssuerSigningKeys = _options.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = _options.ClockSkew,
        };

        TokenValidationResult result = await _handler.ValidateTokenAsync(credential.Token, parameters)
            .ConfigureAwait(false);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            throw new UnauthenticatedException(
                "JWT validation failed.",
                result.Exception);
        }
        cancellationToken.ThrowIfCancellationRequested();

        string? subject = jwt.Subject;
        if (string.IsNullOrEmpty(subject))
        {
            throw new UnauthenticatedException("JWT missing required \"sub\" claim.");
        }

        var claims = result.Claims is null
            ? null
            : (IReadOnlyDictionary<string, object?>)result.Claims;
        return new AuthIdentity(subject, claims);
    }
}
