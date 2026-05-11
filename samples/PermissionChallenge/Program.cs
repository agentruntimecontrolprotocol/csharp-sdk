// Generator proposes; reviewer holds veto via permission.request.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Permissions;
using ARCP.Samples.PermissionChallenge;
using static ARCP.Samples.PermissionChallenge.ClientStubs;
using Env = ARCP.Envelope.Envelope;

const int MaxRevisions = 4;

static string Fingerprint(string diff) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(diff))).ToLowerInvariant()[..16];

static async Task<LeaseId> RequestApplyAsync(ARCPClient client, string ticketId, Patch patch)
{
    string fp = Fingerprint(patch.Diff);
    Env reply = await Request(
        client,
        Envelope(
            client,
            "permission.request",
            new PermissionRequest(
                Permission: "repo.write",
                Resource: $"ticket:{ticketId}/{fp}",
                Operation: "apply_patch",
                Reason: "apply patch",
                RequestedLeaseSeconds: 90),
            // Same key per (ticket, diff): identical patch dedupes at runtime.
            idempotencyKey: new IdempotencyKey($"review:{ticketId}:{fp}")),
        timeout: TimeSpan.FromSeconds(300));
    if (reply.Type == "permission.deny")
    {
        PermissionDeny deny = (PermissionDeny)reply.Payload;
        throw new ARCPException(ErrorCode.PermissionDenied, deny.Reason);
    }
    return ((LeaseGranted)reply.Payload).LeaseId;
}

static async Task RespondAsync(ARCPClient client, Env request, ReviewVerdict verdict)
{
    PermissionRequest req = (PermissionRequest)request.Payload;
    if (verdict.Grant)
    {
        await Send(client, Envelope(
            client,
            "permission.grant",
            new PermissionGrant(
                Permission: req.Permission,
                Resource: req.Resource,
                Operation: req.Operation,
                ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(90)),
            correlationId: request.Id));
    }
    else
    {
        await Send(client, Envelope(
            client,
            "permission.deny",
            new PermissionDeny(req.Permission, req.Resource, req.Operation, verdict.Reason),
            correlationId: request.Id));
    }
}

static async Task ReviewerLoopAsync(ARCPClient reviewer, string ticket, CancellationToken ct)
{
    await foreach (Env env in Events(reviewer, ct))
    {
        if (env.Type == "permission.request")
        {
            ReviewVerdict verdict = await Agents.ReviewAsync(ticket, env);
            await RespondAsync(reviewer, env, verdict);
        }
    }
}

// Two sessions, one per agent. In production they'd be on different
// runtimes; the message contract is identical.
ARCPClient generator = null!; // transport, identity, auth elided
ARCPClient reviewer = null!;
await Open(generator);
await Open(reviewer);

string ticketId = "JIRA-4812";
string ticket = "Reject JWTs whose `aud` does not match the configured audience. Add a unit test.";

using CancellationTokenSource cts = new();
Task reviewerTask = ReviewerLoopAsync(reviewer, ticket, cts.Token);

string? priorDenial = null;
try
{
    for (int i = 0; i < MaxRevisions; i++)
    {
        Patch patch = await Agents.ProposeAsync(ticket, priorDenial);
        try
        {
            LeaseId lease = await RequestApplyAsync(generator, ticketId, patch);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "applied {0} lease={1}", Fingerprint(patch.Diff), lease));
            return;
        }
        catch (ARCPException ex) when (ex.Code == ErrorCode.PermissionDenied)
        {
            priorDenial = ex.Message;
        }
    }
    Console.WriteLine("abandoned after max_revisions");
}
finally
{
    await cts.CancelAsync();
    try { await reviewerTask; } catch (OperationCanceledException) { }
    await generator.CloseAsync();
    await reviewer.CloseAsync();
}
