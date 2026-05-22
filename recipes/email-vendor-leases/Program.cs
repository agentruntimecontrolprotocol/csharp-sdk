// SPDX-License-Identifier: Apache-2.0
// recipes/email-vendor-leases: a triage agent iterates over three tool calls;
// only two are permitted by the caller's lease.  The denied call gets a
// structured ToolError result.  The successful inbox_read also emits a vendor
// extension event that the client can observe alongside standard protocol events.
// Spec §9 (leases), §10 (tool calls), §11 (vendor extensions).
using Arcp.Client;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

using var cts = new CancellationTokenSource();
var ct = cts.Token;

// ── server ────────────────────────────────────────────────────────────────────
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "email-vendor-leases", Version = "1.0.0" },
});

var lm = new Arcp.Runtime.Leases.LeaseManager();

// Three tool calls the agent wants to make; the lease permits only the first two.
var toolCalls = new[]
{
    ("c1", "inbox_read",      (object)new { folder = "INBOX" }),
    ("c2", "attachment_scan", (object)new { mime = "application/pdf" }),
    ("c3", "send_reply",      (object)new { to = "customer@example.com" }),
};

server.RegisterAgent("triage", async (ctx, tct) =>
{
    foreach (var (callId, tool, args) in toolCalls)
    {
        try
        {
            // AuthorizeOperation throws PermissionDeniedException when the tool
            // is not listed in the caller's lease (spec §9.4).
            lm.AuthorizeOperation(ctx.Lease, ctx.LeaseConstraints,
                                  LeaseNamespaces.ToolCall, tool);
        }
        catch (PermissionDeniedException ex)
        {
            await ctx.LogAsync("warn", $"tool '{tool}' denied by lease: {ex.Message}", tct);
            // Return a structured error to the caller instead of throwing.
            await ctx.ToolResultAsync(callId, null,
                new ToolError { Code = ex.Code, Message = ex.Message }, tct);
            continue;
        }

        // Tool is permitted — emit the call event and simulate a local result.
        await ctx.ToolCallAsync(tool, callId, args, tct);

        switch (tool)
        {
            case "inbox_read":
                // Vendor extension event carries parsed metadata (spec §11).
                await ctx.EmitEventAsync("x-vendor.acme.email.parsed",
                    new { folder = "INBOX", count = 42, unread = 7 }, tct);
                await ctx.ToolResultAsync(callId,
                    new { messages = 42, unread = 7 }, null, tct);
                break;

            case "attachment_scan":
                await ctx.ToolResultAsync(callId,
                    new { threats = 0, scanned = 3 }, null, tct);
                break;
        }
    }

    return new { processed = true };
});

// ── client ────────────────────────────────────────────────────────────────────
var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT, ct);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "email-client", Version = "1.0.0" },
});

// Lease grants inbox_read and attachment_scan only — send_reply is intentionally absent.
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.ToolCall] = new[] { "inbox_read", "attachment_scan" },
});

var handle = await client.SubmitAsync(
    "triage",
    input: new { mailbox = "support@example.com" },
    leaseRequest: lease,
    cancellationToken: ct);

// Consume the event stream; print vendor events prominently.
await foreach (var ev in handle.Events())
{
    if (ev.Kind.StartsWith("x-vendor."))
        Console.WriteLine($"VENDOR  {ev.Kind} → {ev.Body.GetRawText()}");
    else
        Console.WriteLine($"        [{ev.Kind}]");
}

var result = await handle.Result;
Console.WriteLine($"triage finished — success: {result.Success}");
return 0;
