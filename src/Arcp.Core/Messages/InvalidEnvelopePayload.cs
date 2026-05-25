// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Serialization;

namespace Arcp.Core.Messages;

/// <summary>Internal payload for the <c>arcp.invalid_envelope</c> sentinel emitted by a transport
/// when a peer sends an envelope that fails to parse. Never transmitted over the wire; the
/// dispatcher converts it into an outbound <c>session.error{INVALID_REQUEST}</c> per spec §12.</summary>
public sealed record InvalidEnvelopePayload
{
    /// <summary>The parse-error message (truncated when logged so as not to echo arbitrary client
    /// content back to peers in the error detail).</summary>
    [JsonPropertyName("parse_error")] public required string ParseError { get; init; }
}
