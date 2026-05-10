using ARCP.Errors;

namespace ARCP.Extensions;

/// <summary>
/// What a receiver does when it encounters an envelope of an unknown
/// <c>type</c>, per RFC-0001-v2 §21.3.
/// </summary>
public enum UnknownTypeDispositionKind
{
    /// <summary>Silently drop the envelope; the sender flagged it optional.</summary>
    Drop,

    /// <summary>Reply with <c>nack</c> and <see cref="ErrorCode.Unimplemented" />.</summary>
    Nack,
}

/// <summary>
/// The decision returned by <see cref="ExtensionDispatch.Classify" />.
/// </summary>
/// <param name="Kind">Disposition kind.</param>
/// <param name="Reason">Free-form reason text describing the decision.</param>
/// <param name="Code">Canonical error code (only meaningful when <see cref="Kind" /> is <see cref="UnknownTypeDispositionKind.Nack" />).</param>
public sealed record UnknownTypeDisposition(
    UnknownTypeDispositionKind Kind,
    string Reason,
    ErrorCode Code = ErrorCode.Unimplemented);

/// <summary>
/// Implements the §21.3 unknown-message dispatch rules.
/// </summary>
public static class ExtensionDispatch
{
    /// <summary>
    /// Decide what to do when we receive an envelope with an unknown <c>type</c>:
    /// <list type="bullet">
    /// <item>Unknown core-prefixed type → <c>nack UNIMPLEMENTED</c>.</item>
    /// <item>Namespaced extension not advertised, sender flagged
    ///       <c>extensions.optional: true</c> → silent <c>drop</c>.</item>
    /// <item>Namespaced extension not advertised, no optional flag →
    ///       <c>nack UNIMPLEMENTED</c>.</item>
    /// <item>Anything else → <c>nack UNIMPLEMENTED</c>.</item>
    /// </list>
    /// </summary>
    /// <param name="type">The unknown wire type.</param>
    /// <param name="extensions">The envelope <c>extensions</c> object, if any.</param>
    /// <returns>The disposition.</returns>
    public static UnknownTypeDisposition Classify(
        string type,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (ExtensionNamespace.LooksLikeCoreType(type))
        {
            return new UnknownTypeDisposition(
                UnknownTypeDispositionKind.Nack,
                $"Unknown core message type \"{type}\" (§21.3)");
        }

        if (ExtensionNamespace.IsValid(type))
        {
            bool optional = extensions is not null
                && extensions.TryGetValue("optional", out object? value)
                && value is true;
            if (optional)
            {
                return new UnknownTypeDisposition(
                    UnknownTypeDispositionKind.Drop,
                    $"Optional extension \"{type}\" not advertised (§21.3)");
            }
            return new UnknownTypeDisposition(
                UnknownTypeDispositionKind.Nack,
                $"Required extension \"{type}\" not advertised (§21.3)");
        }

        return new UnknownTypeDisposition(
            UnknownTypeDispositionKind.Nack,
            $"Type \"{type}\" matches neither core nor extension namespace (§21.3)");
    }
}
