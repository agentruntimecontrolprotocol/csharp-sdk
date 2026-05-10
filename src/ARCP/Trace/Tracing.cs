namespace ARCP.Trace;

/// <summary>
/// <see cref="System.Threading.AsyncLocal{T}" />-backed trace context flow.
/// On each inbound envelope receive the runtime calls
/// <see cref="RunWithAsync" /> with the envelope's
/// <see cref="TraceContext" /> so downstream <c>await</c>s inherit it
/// automatically (§17.1).
/// </summary>
public static class Tracing
{
    private static readonly AsyncLocal<TraceContext?> Local = new();

    /// <summary>
    /// The current trace context, or <see langword="null" /> when no
    /// envelope is being processed on this async flow.
    /// </summary>
    public static TraceContext? Current => Local.Value;

    /// <summary>
    /// Run <paramref name="body" /> with <paramref name="context" /> as the
    /// active <see cref="Current" /> trace context for the duration of the
    /// async execution.
    /// </summary>
    /// <param name="context">The trace context to flow.</param>
    /// <param name="body">The async work.</param>
    /// <returns>A task representing the completion of <paramref name="body" />.</returns>
    public static async Task RunWithAsync(TraceContext context, Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        TraceContext? prev = Local.Value;
        Local.Value = context;
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            Local.Value = prev;
        }
    }

    /// <summary>
    /// Run <paramref name="body" /> with <paramref name="context" /> as the
    /// active <see cref="Current" /> trace context for the duration of the
    /// async execution; returns the result of <paramref name="body" />.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="context">The trace context to flow.</param>
    /// <param name="body">The async work.</param>
    /// <returns>The result of <paramref name="body" />.</returns>
    public static async Task<T> RunWithAsync<T>(TraceContext context, Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        TraceContext? prev = Local.Value;
        Local.Value = context;
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            Local.Value = prev;
        }
    }
}
