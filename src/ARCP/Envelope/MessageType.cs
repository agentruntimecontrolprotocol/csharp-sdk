using System.Text.Json.Serialization;

namespace ARCP.Envelope;

/// <summary>
/// Abstract base for every typed message payload in the <c>ARCP.Messages</c>
/// namespace tree. Each concrete subtype overrides <see cref="WireType" />
/// with its canonical RFC-0001-v2 §6.2 type string (e.g. <c>"session.open"</c>)
/// and is registered in <see cref="MessageTypeRegistry" />.
/// </summary>
/// <remarks>
/// Polymorphic serialization is handled by
/// <see cref="EnvelopeJsonConverter" /> rather than
/// <c>[JsonPolymorphic]</c>. The wire format places the discriminator on the
/// envelope itself (<see cref="Envelope.Type" />), not nested inside the
/// payload — see PLAN.md §6 ("Polymorphic <c>[JsonDerivedType]</c> for envelope
/// dispatch").
/// </remarks>
public abstract record MessageType
{
    /// <summary>
    /// The canonical wire-form type discriminator for this payload, e.g.
    /// <c>"session.open"</c>. Mirrored into the envelope's <c>type</c> field
    /// during serialization.
    /// </summary>
    [JsonIgnore]
    public abstract string WireType { get; }
}
