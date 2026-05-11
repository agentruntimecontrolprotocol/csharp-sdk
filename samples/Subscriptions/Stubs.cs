// Elision helpers. Real SDK supplies these via the public API.
using System.Runtime.CompilerServices;
using ARCP.Client;
using ARCP.Messages.Session;
using Env = ARCP.Envelope.Envelope;
using MsgType = ARCP.Envelope.MessageType;

namespace ARCP.Samples.Subscriptions;

internal static class ClientStubs
{
    public static Task<SessionAccepted> Open(ARCPClient client) =>
        throw new NotImplementedException();

    public static Task Send(ARCPClient client, Env envelope) =>
        throw new NotImplementedException();

    public static Task<Env> Request(
        ARCPClient client, Env envelope, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

#pragma warning disable CS1998
    public static async IAsyncEnumerable<Env> Events(
        ARCPClient client,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield break;
    }
#pragma warning restore CS1998

    public static Env Envelope(
        ARCPClient client,
        string type,
        MsgType payload,
        ARCP.Ids.JobId? jobId = null,
        ARCP.Ids.StreamId? streamId = null,
        ARCP.Ids.SubscriptionId? subscriptionId = null,
        ARCP.Ids.TraceId? traceId = null,
        ARCP.Ids.MessageId? correlationId = null,
        ARCP.Ids.IdempotencyKey? idempotencyKey = null) =>
        throw new NotImplementedException();
}

internal static class EnvelopeStubs
{
    public static Env FromWire(System.Text.Json.JsonElement wire) =>
        throw new NotImplementedException();
}
