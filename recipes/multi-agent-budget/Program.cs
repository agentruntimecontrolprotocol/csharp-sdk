// SPDX-License-Identifier: Apache-2.0
// recipes/multi-agent-budget: a planner agent splits a research topic into sub-questions
// and dispatches each to a worker agent under a strict USD budget cap.  The planner uses
// the "debit-self-for-each-grant" pattern — ctx.MetricAsync is called immediately after
// each delegation so that ctx.Budget accurately reflects remaining funds on every
// subsequent iteration.  Spec §13.2 (budget cascade), §9.6 (lease subset checks).
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
    Runtime = new RuntimeInfo { Name = "multi-agent-budget", Version = "1.0.0" },
});

// ── worker agent ──────────────────────────────────────────────────────────────
// Simulates a research sub-agent.  It debits its own per-question token cost so
// that the child budget is consumed and visible in the event stream.
server.RegisterAgent("worker", async (ctx, wct) =>
{
    var q = ctx.Input.GetProperty("q").GetString() ?? "unknown";
    await ctx.LogAsync("info", $"researching: {q}", wct);
    await Task.Delay(40, wct);
    // Charge token cost against the child budget granted by the planner.
    await ctx.MetricAsync("cost.tokens", 1.50, "USD", cancellationToken: wct);
    return new { answer = $"findings for: {q}" };
});

// Dedicated in-process session for planner → worker fan-out.
// Each server.AcceptAsync call handles exactly one session; the workerClient
// can submit many sequential or concurrent jobs through the same session.
var (workerClientT, workerServerT) = MemoryTransport.Pair();
_ = server.AcceptAsync(workerServerT, ct);
await using var workerClient = await ArcpClient.ConnectAsync(workerClientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "planner-worker-client", Version = "1.0.0" },
});

// ── planner agent ─────────────────────────────────────────────────────────────
// Registered after workerClient so the closure captures an already-connected client.
server.RegisterAgent("planner", async (ctx, pct) =>
{
    var questions = new[]
    {
        "climate trends 2024",
        "ocean energy storage",
        "grid-scale batteries",  // this one will be skipped — budget exhausted after two
    };

    const double sliceUsd = 2.00;
    var answers = new List<object>();

    for (var i = 0; i < questions.Length; i++)
    {
        // ── debit-self-for-each-grant: check remaining budget before delegation ──
        var remaining = (double)ctx.Budget["USD"];
        if (remaining < sliceUsd)
        {
            await ctx.LogAsync("warn",
                $"skipping '{questions[i]}' — budget cap reached (${remaining:F2} remaining)", pct);
            continue;
        }

        // Grant a per-question sub-budget to the worker (child ⊆ parent's remaining).
        var childLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            ["cost.budget"] = new[] { $"USD:{sliceUsd:F2}" },
        });

        // Record the delegation in the parent job's event stream (spec §13).
        await ctx.DelegateAsync($"worker-{i:000}", "worker", new { q = questions[i] }, pct);

        // Debit the planner's own budget immediately after granting the child lease.
        // This ensures the next iteration's ctx.Budget check reflects the outflow.
        await ctx.MetricAsync("cost.delegate", sliceUsd, "USD", cancellationToken: pct);

        // Dispatch the worker job through the dedicated in-process session.
        var wh = await workerClient.SubmitAsync(
            "worker",
            input: new { q = questions[i] },
            leaseRequest: childLease,
            cancellationToken: pct);

        var wr = await wh.Result;
        await ctx.LogAsync("info", $"worker-{i}: done (success={wr.Success})", pct);
        answers.Add(new { q = questions[i], success = wr.Success });
    }

    return new { answers };
});

// ── orchestrator client ───────────────────────────────────────────────────────
var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT, ct);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "orchestrator-client", Version = "1.0.0" },
});

// USD 5.00 total: enough for two $2.00 slices; the third question is skipped
// because only $1.00 remains after the first two debits.
var plannerLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    ["cost.budget"] = new[] { "USD:5.00" },
    // Spec §9.3 deny-by-default: the planner must hold agent.delegate to delegate to workers.
    ["agent.delegate"] = new[] { "*" },
});

var handle = await client.SubmitAsync(
    "planner",
    input: new { topic = "renewable energy" },
    leaseRequest: plannerLease,
    cancellationToken: ct);

// Print every event from the planner job while it runs.
_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
        Console.WriteLine($"  [{ev.Kind}] {ev.Body.GetRawText()}");
}, ct);

var result = await handle.Result;
Console.WriteLine($"planner finished — success: {result.Success}");
return 0;
