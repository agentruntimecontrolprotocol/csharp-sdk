// Generic envelope payload for arcpx.sdr.* messages. Real implementations
// register strongly-typed records via `ExtensionRegistry`.
using System.Text.Json;
using ARCP.Envelope;

namespace ARCP.Samples.Extensions;

public sealed record ExtensionPayload(JsonElement Value) : MessageType
{
    public override string WireType => "arcpx.sdr";
}
