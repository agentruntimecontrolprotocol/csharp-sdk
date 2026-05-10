namespace ARCP.Ids;

/// <summary>
/// Shared validation helpers for newtype identifiers in <see cref="ARCP.Ids" />.
/// </summary>
internal static class IdValidation
{
    /// <summary>
    /// Throws <see cref="ArgumentException" /> if <paramref name="value" /> is
    /// <see langword="null" /> or empty; returns it otherwise so callers can
    /// chain <c>new SessionId(EnsureNotEmpty(...))</c>.
    /// </summary>
    /// <param name="value">The candidate id string.</param>
    /// <param name="paramName">The parameter name to attribute the exception to.</param>
    /// <returns><paramref name="value" /> unchanged.</returns>
    public static string EnsureNotEmpty(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Id value must be non-empty.", paramName);
        }

        return value;
    }
}
