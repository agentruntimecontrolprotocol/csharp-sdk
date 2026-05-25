// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>Payload for the <c>session.resume</c> message (spec §6.3). A client reconnecting after
/// a transport drop presents its most recent <c>resume_token</c> and the highest <c>event_seq</c>
/// it has observed; the runtime replays buffered events with <c>seq &gt; last_event_seq</c>.</summary>
public sealed record SessionResumePayload
{
    /// <summary>The resume token issued in the prior <c>session.welcome</c>.</summary>
    [JsonPropertyName("resume_token")] public required string ResumeToken { get; init; }

    /// <summary>The highest <c>event_seq</c> the client has observed. The runtime replays buffered
    /// events with <c>seq &gt; last_event_seq</c>; if the buffer no longer covers that point, the
    /// runtime returns <c>RESUME_WINDOW_EXPIRED</c>.</summary>
    [JsonPropertyName("last_event_seq")] public long? LastEventSeq { get; init; }
}
