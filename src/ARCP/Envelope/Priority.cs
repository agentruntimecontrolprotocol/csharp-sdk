using System.Text.Json.Serialization;

namespace ARCP.Envelope;

/// <summary>
/// Envelope priority per RFC-0001-v2 §6.5. <c>critical</c> is reserved for
/// messages that must not be deferred (e.g. <c>permission.request</c>,
/// terminal job events).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Priority>))]
public enum Priority
{
    /// <summary>Lowest scheduling priority; first to be shed under backpressure.</summary>
    [JsonStringEnumMemberName("low")]
    Low,

    /// <summary>Default priority when not specified.</summary>
    [JsonStringEnumMemberName("normal")]
    Normal,

    /// <summary>Elevated priority.</summary>
    [JsonStringEnumMemberName("high")]
    High,

    /// <summary>
    /// Reserved for messages that must not be deferred (e.g. permission challenges
    /// blocking real human action, terminal job events). Implementations
    /// **MAY** rate-limit critical traffic from misbehaving clients.
    /// </summary>
    [JsonStringEnumMemberName("critical")]
    Critical,
}
