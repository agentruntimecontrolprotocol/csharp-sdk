using ARCP.Messages.Session;

namespace ARCP.Auth;

/// <summary>
/// Result of <see cref="IAuthVerifier.VerifyAsync" />.
/// </summary>
/// <param name="Principal">The authenticated principal name (e.g. JWT subject, bearer-token user).</param>
/// <param name="Claims">Optional structured claims surfaced for downstream policy.</param>
public sealed record AuthIdentity(
    string Principal,
    IReadOnlyDictionary<string, object?>? Claims = null);

/// <summary>
/// Verifies <see cref="AuthCredential" />s presented in <c>session.open</c>
/// per RFC-0001-v2 §8.2.
/// </summary>
public interface IAuthVerifier
{
    /// <summary>The scheme this verifier handles.</summary>
    AuthScheme Scheme { get; }

    /// <summary>
    /// Verify <paramref name="credential" /> and return a typed
    /// <see cref="AuthIdentity" /> on success.
    /// </summary>
    /// <param name="credential">The credential block.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verified identity.</returns>
    /// <exception cref="ARCP.Errors.UnauthenticatedException">If verification fails.</exception>
    Task<AuthIdentity> VerifyAsync(
        AuthCredential credential,
        CancellationToken cancellationToken = default);
}
