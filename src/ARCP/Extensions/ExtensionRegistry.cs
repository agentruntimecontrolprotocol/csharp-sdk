using System.Collections.Concurrent;
using System.Text.Json;
using ARCP.Errors;

namespace ARCP.Extensions;

/// <summary>
/// Registry of extension message-type schemas (§21). An extension provides a
/// CLR <see cref="Type" /> that its payload deserializes into; the registry
/// validates the namespace and stores the binding. Runtime/client dispatch
/// looks up the type before parsing extension messages.
/// </summary>
/// <remarks>
/// This is per-instance (not static) — every <c>ARCPRuntime</c> and
/// <c>ARCPClient</c> owns its own registry, matching the "no static mutable
/// state" rule in PLAN.md §6. Concurrent access is safe.
/// </remarks>
public sealed class ExtensionRegistry
{
    private readonly ConcurrentDictionary<string, Type> _bindings = new(StringComparer.Ordinal);

    /// <summary>Whether this registry currently knows about <paramref name="name" />.</summary>
    /// <param name="name">The extension namespace.</param>
    /// <returns><see langword="true" /> if registered.</returns>
    public bool Has(string name) =>
        !string.IsNullOrEmpty(name) && _bindings.ContainsKey(name);

    /// <summary>The names of all registered extensions, in insertion order.</summary>
    /// <returns>The set of registered names.</returns>
    public IReadOnlyCollection<string> List() => _bindings.Keys.ToArray();

    /// <summary>
    /// Register an extension type with its payload CLR type. The CLR type
    /// must be a record or class deserializable by <see cref="JsonSerializer" />.
    /// </summary>
    /// <typeparam name="TPayload">The CLR type for the extension payload.</typeparam>
    /// <param name="name">The extension namespace (§21.1).</param>
    /// <exception cref="InvalidArgumentException">
    /// If <paramref name="name" /> is not a valid namespace.
    /// </exception>
    public void Register<TPayload>(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!ExtensionNamespace.IsValid(name))
        {
            throw new InvalidArgumentException(
                $"Cannot register \"{name}\": not a valid extension namespace (§21.1).");
        }
        _bindings[name] = typeof(TPayload);
    }

    /// <summary>
    /// Look up the payload type for a registered extension.
    /// </summary>
    /// <param name="name">The extension namespace.</param>
    /// <returns>The bound type, or <see langword="null" /> if not registered.</returns>
    public Type? Resolve(string name) =>
        string.IsNullOrEmpty(name) ? null : (_bindings.TryGetValue(name, out Type? t) ? t : null);

    /// <summary>
    /// Parse an extension payload to the registered CLR type.
    /// </summary>
    /// <param name="name">The extension namespace.</param>
    /// <param name="payload">The JSON payload as a <see cref="JsonElement" />.</param>
    /// <param name="options">Serializer options, or <see langword="null" /> for default.</param>
    /// <returns>The parsed payload, or <see langword="null" /> if the wire payload was JSON null.</returns>
    /// <exception cref="UnimplementedException">If <paramref name="name" /> is not registered.</exception>
    /// <exception cref="InvalidArgumentException">If the JSON cannot be deserialized to the registered type.</exception>
    public object? Parse(string name, JsonElement payload, JsonSerializerOptions? options = null)
    {
        Type type = Resolve(name)
            ?? throw new UnimplementedException("§21.2", $"Extension \"{name}\" is not registered.");

        try
        {
            return payload.Deserialize(type, options);
        }
        catch (JsonException ex)
        {
            throw new InvalidArgumentException(
                $"Failed to deserialize extension \"{name}\": {ex.Message}", ex);
        }
    }

    /// <summary>Remove a registered extension. Returns whether it had been registered.</summary>
    /// <param name="name">The extension namespace.</param>
    /// <returns><see langword="true" /> if the registry contained it.</returns>
    public bool Unregister(string name) =>
        !string.IsNullOrEmpty(name) && _bindings.TryRemove(name, out _);
}
