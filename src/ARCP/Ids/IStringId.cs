namespace ARCP.Ids;

/// <summary>
/// Marker interface implemented by every newtype identifier in
/// <see cref="ARCP.Ids" />. The underlying <see cref="Value" /> string is the
/// canonical wire representation per RFC-0001-v2 §6.1.1.
/// </summary>
/// <remarks>
/// Implementations are <c>readonly record struct</c> values that compose a
/// single string. The static factory <see cref="FromString" /> is used by
/// <see cref="StringIdJsonConverter{T}" /> to round-trip ids as bare JSON
/// strings (not as objects with a <c>Value</c> property).
/// </remarks>
/// <typeparam name="TSelf">The implementing id record struct.</typeparam>
public interface IStringId<TSelf>
    where TSelf : struct, IStringId<TSelf>
{
    /// <summary>The wire-form string value of this identifier.</summary>
    string Value { get; }

    /// <summary>
    /// Construct an instance from its wire-form string. Implementations
    /// must reject <see langword="null" /> or empty input with
    /// <see cref="ArgumentException" />.
    /// </summary>
    /// <param name="value">The wire string.</param>
    /// <returns>The parsed id.</returns>
    static abstract TSelf FromString(string value);
}
