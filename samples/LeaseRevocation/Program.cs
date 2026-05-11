// Warehouse DB admin agent. Reads pre-granted; writes prompt operator.
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Permissions;
using ARCP.Samples.LeaseRevocation;
using static ARCP.Samples.LeaseRevocation.ClientStubs;
using Env = ARCP.Envelope.Envelope;

string[] preGranted = ["public.orders", "public.customers", "warehouse.fct_revenue_daily"];
const int ReadLeaseSeconds = 60 * 60;
const int WriteLeaseSeconds = 5 * 60;

static async Task<(LeaseId Id, DateTimeOffset ExpiresAt)> RequestLeaseAsync(
    ARCPClient client,
    string permission,
    string table,
    string operation,
    int seconds,
    string reason)
{
    Env reply = await Request(
        client,
        Envelope(client, "permission.request", new PermissionRequest(
            Permission: permission,
            Resource: $"table:{table}",
            Operation: operation,
            Reason: reason,
            RequestedLeaseSeconds: seconds)),
        timeout: TimeSpan.FromSeconds(180));
    if (reply.Type == "permission.deny")
    {
        throw new ARCPException(ErrorCode.PermissionDenied, $"{permission} denied on {table}");
    }
    LeaseGranted granted = (LeaseGranted)reply.Payload;
    return (granted.LeaseId, granted.ExpiresAt);
}

static async Task<string> AuthorizeAsync(
    ARCPClient client,
    string sql,
    Dictionary<(string Table, string Op), (LeaseId Id, DateTimeOffset ExpiresAt)> leases)
{
    StatementClass klass = Sql.Classify(sql);
    if (klass.Tables.Count == 0)
    {
        throw new ARCPException(ErrorCode.InvalidArgument, "no table referenced");
    }
    int seconds = klass.Op == "read" ? ReadLeaseSeconds : WriteLeaseSeconds;
    foreach (string table in klass.Tables)
    {
        if (leases.TryGetValue((table, klass.Op), out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            continue;
        }
        leases[(table, klass.Op)] = await RequestLeaseAsync(
            client,
            permission: $"db.{klass.Op}",
            table: table,
            operation: klass.Op,
            seconds: seconds,
            reason: $"{klass.Op.ToUpperInvariant()} on {table}: {sql[..Math.Min(80, sql.Length)]}");
    }
    return klass.Op;
}

static void HandleInbound(Env env, Dictionary<(string, string), (LeaseId Id, DateTimeOffset ExpiresAt)> leases)
{
    // Wire `lease.revoked` into the cache so the next call re-prompts.
    if (env.Type != "lease.revoked") return;
    LeaseRevoked revoked = (LeaseRevoked)env.Payload;
    foreach (var (k, v) in leases.ToArray())
    {
        if (v.Id.Equals(revoked.LeaseId))
        {
            leases.Remove(k);
        }
    }
}

ARCPClient client = null!; // transport, identity, auth elided
await Open(client);

Dictionary<(string Table, string Op), (LeaseId Id, DateTimeOffset ExpiresAt)> leases = new();
using CancellationTokenSource drainCts = new();

async Task DrainAsync()
{
    await foreach (Env env in Events(client, drainCts.Token))
    {
        HandleInbound(env, leases);
    }
}

Task drain = DrainAsync();

// Pre-grant the broad reads at session open.
foreach (string table in preGranted)
{
    leases[(table, "read")] = await RequestLeaseAsync(
        client, "db.read", table, "read", ReadLeaseSeconds, "bootstrap");
}

// SELECT — covered by the bootstrap lease.
await AuthorizeAsync(
    client,
    "SELECT count(*) FROM public.orders WHERE shipped_at::date = current_date - 1",
    leases);
// UPDATE — triggers permission.request; operator must approve.
await AuthorizeAsync(
    client,
    "UPDATE public.orders SET status='refunded' WHERE id=4812",
    leases);

await drainCts.CancelAsync();
try { await drain; } catch (OperationCanceledException) { }
await client.CloseAsync();
