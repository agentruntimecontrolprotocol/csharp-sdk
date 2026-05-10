using ARCP.Errors;
using ARCP.Messages.Session;

namespace ARCP.Auth;

/// <summary>
/// Bearer-token verifier per RFC-0001-v2 §8.2. The supplied
/// <see cref="BearerTokenStore" /> is consulted at verification time —
/// callers plug in their own trust store (e.g. an in-memory dictionary in
/// tests, or a database lookup in production).
/// </summary>
public sealed class BearerAuth : IAuthVerifier
{
    private readonly BearerTokenStore _store;

    /// <summary>Initializes a verifier with the given token-store delegate.</summary>
    /// <param name="store">Delegate that maps a token to a principal name (or <see langword="null" />).</param>
    public BearerAuth(BearerTokenStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public AuthScheme Scheme => AuthScheme.Bearer;

    /// <inheritdoc />
    public async Task<AuthIdentity> VerifyAsync(
        AuthCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.Scheme != AuthScheme.Bearer)
        {
            throw new UnauthenticatedException($"BearerAuth cannot verify scheme {credential.Scheme}.");
        }
        if (string.IsNullOrEmpty(credential.Token))
        {
            throw new UnauthenticatedException("Bearer auth requires a non-empty token.");
        }

        string? principal = await _store(credential.Token, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(principal))
        {
            throw new UnauthenticatedException("Bearer token is not recognized.");
        }
        return new AuthIdentity(principal);
    }
}

/// <summary>
/// Delegate for resolving a bearer token to a principal name. Returns
/// <see langword="null" /> for unknown tokens.
/// </summary>
/// <param name="token">The presented bearer token.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>The principal name, or <see langword="null" /> if invalid.</returns>
public delegate Task<string?> BearerTokenStore(string token, CancellationToken cancellationToken);
