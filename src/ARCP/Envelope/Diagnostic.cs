namespace ARCP.Envelope;

/// <summary>
/// Phase-1-only payload types kept here so the envelope round-trip tests can
/// exercise the full converter pipeline before the real <c>ARCP.Messages</c>
/// records exist (Phase 2). The single concrete type,
/// <see cref="Diagnostic.PingPayload" />, models the empty <c>ping</c>
/// payload (§6.2 control set).
/// </summary>
public static class Diagnostic
{
    /// <summary>
    /// Empty <c>ping</c> payload per §6.2. Used as the smoke-test message in
    /// Phase 1. Replaced by <c>ARCP.Messages.Control.Ping</c> in Phase 2; the
    /// type kept under <c>Envelope.Diagnostic</c> is private to the SDK and
    /// not part of the published API surface.
    /// </summary>
    public sealed record PingPayload : MessageType
    {
        /// <inheritdoc />
        public override string WireType => "ping";
    }
}
