using ARCP.Auth;
using ARCP.Ids;
using ARCP.Messages.Session;

namespace ARCP.Runtime;

/// <summary>
/// Per-transport session state machine state per RFC-0001-v2 §8.1. Modeled
/// as a sealed type hierarchy so handlers can pattern-match exhaustively.
/// </summary>
public abstract record SessionState
{
    /// <summary>Initial state: no session has been opened on this transport.</summary>
    public sealed record Unauthenticated : SessionState;

    /// <summary>
    /// Mid-handshake: <c>session.open</c> received and accepted in principle,
    /// awaiting client response to <c>session.challenge</c> (or directly
    /// transitioning to <see cref="Authenticated" /> when no challenge is
    /// required).
    /// </summary>
    /// <param name="Open">The original <c>session.open</c> envelope payload.</param>
    public sealed record Authenticating(SessionOpen Open) : SessionState;

    /// <summary>Session established (§8.3).</summary>
    /// <param name="SessionId">The session id.</param>
    /// <param name="Identity">The verified principal.</param>
    /// <param name="NegotiatedCapabilities">The agreed capability set.</param>
    public sealed record Authenticated(
        SessionId SessionId,
        AuthIdentity Identity,
        Capabilities NegotiatedCapabilities) : SessionState;

    /// <summary>Closed, evicted, or rejected.</summary>
    /// <param name="Reason">The reason for closure (optional).</param>
    public sealed record Closed(string? Reason = null) : SessionState;
}
