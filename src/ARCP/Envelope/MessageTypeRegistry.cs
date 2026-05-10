using System.Collections.Concurrent;

namespace ARCP.Envelope;

/// <summary>
/// Bidirectional registry mapping wire <c>type</c> discriminators (e.g.
/// <c>"session.open"</c>) to <see cref="MessageType" /> CLR types.
/// </summary>
/// <remarks>
/// <para>
/// Per-instance, not static. The default
/// <see cref="MessageTypeRegistry.CoreCatalog" /> is populated at runtime
/// startup (Phase 2 onwards) from concrete message records. Extension types
/// are added through <see cref="ARCP.Extensions.ExtensionRegistry" />; the
/// <c>EnvelopeJsonConverter</c> consults both.
/// </para>
/// <para>
/// In Phase 1 only the test-only <see cref="Diagnostic.PingPayload" /> is
/// pre-registered — the full set arrives in Phase 2.
/// </para>
/// </remarks>
public sealed class MessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _wireToClr = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _clrToWire = new();

    /// <summary>
    /// Register a concrete <see cref="MessageType" /> subtype with its wire
    /// discriminator. Ignored if already registered with the same mapping.
    /// </summary>
    /// <typeparam name="T">The concrete payload type.</typeparam>
    /// <param name="wireType">The canonical type string (e.g. <c>"session.open"</c>).</param>
    /// <exception cref="InvalidOperationException">If <paramref name="wireType" /> is already bound to a different CLR type.</exception>
    public void Register<T>(string wireType)
        where T : MessageType
    {
        ArgumentException.ThrowIfNullOrEmpty(wireType);
        if (_wireToClr.TryGetValue(wireType, out Type? existing) && existing != typeof(T))
        {
            throw new InvalidOperationException(
                $"MessageTypeRegistry: \"{wireType}\" already bound to {existing.Name}, refusing to rebind to {typeof(T).Name}.");
        }
        _wireToClr[wireType] = typeof(T);
        _clrToWire[typeof(T)] = wireType;
    }

    /// <summary>
    /// Resolve a CLR type for a wire discriminator.
    /// </summary>
    /// <param name="wireType">The wire string.</param>
    /// <returns>The CLR type, or <see langword="null" /> if not registered.</returns>
    public Type? Resolve(string wireType) =>
        string.IsNullOrEmpty(wireType) ? null : _wireToClr.GetValueOrDefault(wireType);

    /// <summary>Whether <paramref name="wireType" /> is registered.</summary>
    /// <param name="wireType">The wire string.</param>
    /// <returns><see langword="true" /> if registered.</returns>
    public bool Contains(string wireType) =>
        !string.IsNullOrEmpty(wireType) && _wireToClr.ContainsKey(wireType);

    /// <summary>The number of registered message types.</summary>
    public int Count => _wireToClr.Count;

    /// <summary>
    /// The canonical core catalog. Phase 2 populates this; Phase 1 only
    /// includes <see cref="Diagnostic.PingPayload" /> for envelope round-trip
    /// tests.
    /// </summary>
    /// <returns>A new registry pre-populated with core types.</returns>
    public static MessageTypeRegistry CoreCatalog()
    {
        MessageTypeRegistry r = new();
        r.Register<Diagnostic.PingPayload>("ping");
        return r;
    }
}
