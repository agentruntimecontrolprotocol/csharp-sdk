namespace ARCP;

/// <summary>
/// Static identifiers for the implemented protocol revision (§1, §6.1.1).
/// </summary>
public static class ProtocolVersion
{
    /// <summary>
    /// The wire-level protocol version emitted in the envelope's <c>arcp</c>
    /// field per §6.1.1.
    /// </summary>
    public const string Wire = "1.0";

    /// <summary>
    /// The implementation version of this SDK. Bumped manually per release.
    /// </summary>
    public const string Sdk = "0.1.0-dev";
}
