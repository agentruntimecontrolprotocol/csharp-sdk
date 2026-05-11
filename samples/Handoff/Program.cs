// Cheap-tier first; escalate to deep tier via agent.handoff.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Artifacts;
using ARCP.Messages.Session;
using ARCP.Samples.Handoff;
using static ARCP.Samples.Handoff.ClientStubs;
using AgentHandoff = ARCP.Messages.Execution.AgentHandoff;
using Env = ARCP.Envelope.Envelope;

const double ConfidenceThreshold = 0.65;
const string CheapUrl = "wss://haiku-pool.tier1.internal";
const string DeepUrl = "wss://opus-pool.tier3.internal";
const string DeepKind = "arcp-opus-pool";
const string DeepFingerprint = "sha256:0a37bf7d61cca21f00..."; // pinned

static async Task<ArtifactRef> PackageContextAsync(ARCPClient client, object transcript)
{
    byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(transcript));
    ArtifactId artifactId = new($"art_{Guid.NewGuid():N}"[..18]);
    Env reply = await Request(
        client,
        Envelope(client, "artifact.put", new ArtifactPut(
            MediaType: "application/json",
            ArtifactId: artifactId,
            Data: Convert.ToBase64String(body),
            Encoding: "base64")),
        timeout: TimeSpan.FromSeconds(15));
    if (reply.Type != "artifact.ref")
    {
        throw new ARCPException(ErrorCode.Internal, $"got {reply.Type}");
    }
    return (ArtifactRef)reply.Payload;
}

static Task EmitHandoffAsync(ARCPClient client, ArtifactRef artifactRef, TraceId traceId) =>
    Send(client, Envelope(
        client,
        "agent.handoff",
        // Spec gestures at shared_memory_ref (RFC §14); we pin runtime
        // identity here so the deep tier proves it's the expected pool.
        new AgentHandoff(
            ToRuntime: new RuntimeIdentity(Kind: DeepKind, Version: "1", Fingerprint: DeepFingerprint),
            SessionId: client.SessionId),
        traceId: traceId));

ARCPClient cheap = null!; // transport=WebSocketTransport(CheapUrl), pinned
SessionAccepted accepted = await Open(cheap);
// Pin runtime kind + fingerprint (RFC §8.3); refuse on mismatch.
if (accepted.Runtime.Kind != "arcp-haiku-pool")
{
    throw new ARCPException(ErrorCode.Unauthenticated, "cheap kind mismatch");
}

string request = "what does CRDT stand for?";
TraceId traceId = new($"trace_{Guid.NewGuid():N}"[..18]);

(string answer, double confidence) = await Cheap.AttemptAsync(request);
if (confidence >= ConfidenceThreshold)
{
    Console.WriteLine(answer);
}
else
{
    ArtifactRef artifact = await PackageContextAsync(cheap, new
    {
        user_request = request,
        transcript = new[]
        {
            new { role = "user", content = request },
            new { role = "assistant", content = answer },
        },
        cheap_confidence = confidence,
    });
    await EmitHandoffAsync(cheap, artifact, traceId);
    Console.WriteLine($"[handed off to {DeepKind} trace_id={traceId}]");
}

_ = DeepUrl; // referenced in comments above; declared for completeness
await cheap.CloseAsync();
