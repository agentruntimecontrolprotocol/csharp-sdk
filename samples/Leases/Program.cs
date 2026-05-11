// Sandboxed on-call agent. Lease-gated shell, reasoning streamed.
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Permissions;
using ARCP.Messages.Streaming;
using ARCP.Samples.Leases;
using static ARCP.Samples.Leases.ClientStubs;
using Env = ARCP.Envelope.Envelope;

HashSet<string> readBinaries = ["/usr/bin/journalctl", "/usr/bin/cat", "/usr/bin/ss", "/usr/bin/ps"];
HashSet<string> writeBinaries = ["/usr/bin/systemctl", "/usr/bin/kill"];
const int ReadLeaseSeconds = 30 * 60;
const int WriteLeaseSeconds = 60;

(string Permission, string Resource, string Operation, int Seconds) Classify(IReadOnlyList<string> argv, string host)
{
    string binary = argv[0];
    if (readBinaries.Contains(binary))
    {
        return ("host.read", $"host:{host}", "read", ReadLeaseSeconds);
    }
    if (writeBinaries.Contains(binary))
    {
        string target = binary == "/usr/bin/systemctl" ? argv[2] : argv[1];
        return ("host.write", $"host:{host}/{binary}/{target}", "write", WriteLeaseSeconds);
    }
    throw new ARCPException(ErrorCode.PermissionDenied, $"binary not allowed: {binary}");
}

static async Task<LeaseId> AcquireLeaseAsync(
    ARCPClient client,
    string permission,
    string resource,
    string operation,
    int seconds,
    string reason)
{
    Env reply = await Request(
        client,
        Envelope(client, "permission.request", new PermissionRequest(
            Permission: permission,
            Resource: resource,
            Operation: operation,
            Reason: reason,
            RequestedLeaseSeconds: seconds)),
        timeout: TimeSpan.FromSeconds(120));
    if (reply.Type == "permission.deny")
    {
        PermissionDeny deny = (PermissionDeny)reply.Payload;
        throw new ARCPException(ErrorCode.PermissionDenied, deny.Reason);
    }
    return ((LeaseGranted)reply.Payload).LeaseId;
}

async Task<string> RunCommandAsync(ARCPClient client, IReadOnlyList<string> argv, string reason, string host)
{
    var (permission, resource, operation, seconds) = Classify(argv, host);
    LeaseId lease = await AcquireLeaseAsync(client, permission, resource, operation, seconds, reason);
    // The lease is the only guard. Spawn the subprocess elsewhere.
    return $"<would run [{string.Join(" ", argv)}] under lease {lease}>";
}

static Task EmitThoughtAsync(ARCPClient client, StreamId streamId, long sequence, string text) =>
    Send(client, Envelope(
        client,
        "stream.chunk",
        new StreamChunk { Sequence = sequence, Role = "assistant_thought", Content = text },
        streamId: streamId));

ARCPClient client = null!; // transport, identity (constrained), auth elided
await Open(client);

StreamId streamId = new($"str_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
await Send(client, Envelope(
    client,
    "stream.open",
    new StreamOpen(StreamKind.Thought),
    streamId: streamId));

long seq = 0;
await foreach (LLMStep step in Agent.LlmLoop("api-gateway pod is OOMing every 4 minutes"))
{
    await EmitThoughtAsync(client, streamId, seq++, step.Thought);
    if (step.ToolCall is { } tc)
    {
        try
        {
            await RunCommandAsync(client, tc.Argv, tc.Reason, host: "edge-pod-04");
        }
        catch (ARCPException ex) when (ex.Code == ErrorCode.PermissionDenied)
        {
            continue; // PERMISSION_DENIED feeds back into the next prompt
        }
    }
    if (step.Final is { } final)
    {
        Console.WriteLine(final);
        break;
    }
}

await client.CloseAsync();
