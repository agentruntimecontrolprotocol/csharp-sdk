<h3 align="center">ARCP C# SDK</h3>

<p align="center"><strong>C# SDK for the Agent Runtime Control Protocol (ARCP) — submit, observe, and control long-running agent jobs from C#.</strong></p>

<p align="center">
  <a href="https://www.nuget.org/packages/Arcp"><img alt="NuGet" src="https://img.shields.io/nuget/v/Arcp.svg"></a>
  <a href="https://github.com/agentruntimecontrolprotocol/csharp-sdk/actions/workflows/test.yml"><img alt="CI" src="https://github.com/agentruntimecontrolprotocol/csharp-sdk/actions/workflows/test.yml/badge.svg"></a>
  <a href="https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md"><img alt="ARCP" src="https://img.shields.io/badge/ARCP-v1.1%20draft-blue"></a>
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-Apache--2.0-lightgrey"></a>
</p>

<p align="center">
  <a href="https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md">Specification</a> ·
  <a href="#concepts">Concepts</a> ·
  <a href="#installation">Install</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="docs/">Guides</a> ·
  <a href="docs/">API reference</a>
</p>

---

`Arcp` is the C# / .NET reference implementation of [ARCP](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md), the Agent Runtime Control Protocol. It covers both sides of the wire — `Arcp.Client` for submitting and observing jobs, `Arcp.Runtime` for hosting agents, with `Arcp.AspNetCore`, `Arcp.Hosting`, `Arcp.Otel`, and `Arcp.Cli` rounding out the host integrations — so either side can talk to any conformant peer in any language without hand-rolling the envelope, sequencing, or lease enforcement.

ARCP itself is a transport-agnostic wire protocol for long-running AI agent jobs. It owns the parts of agent infrastructure that don't change between products — sessions, durable event streams, capability leases, budgets, resume — and stays out of the parts that do. ARCP wraps the agent function; it does not define how agents are built, how tools are exposed (that's MCP), or how telemetry is exported (that's OpenTelemetry).

## Installation

Requires .NET 10 or later. The SDK ships as a set of NuGet packages: install the meta-package `Arcp` for everything (client, runtime, and core types), or pick à la carte if you only need one side of the wire:

```sh
dotnet add package Arcp
# or, à la carte:
dotnet add package Arcp.Client    # client side
dotnet add package Arcp.Runtime   # runtime side
dotnet add package Arcp.Core      # wire types only
```

Optional host integrations live in separate packages: `Arcp.AspNetCore` (Kestrel WebSocket endpoint via `IEndpointRouteBuilder.MapArcp`), `Arcp.Hosting` (`IServiceCollection.AddArcpRuntime` for non-ASP.NET workers), `Arcp.Otel` (W3C trace propagation), and `Arcp.Cli` (the `arcp` executable).

## Quick start

Connect to a runtime, submit a job, stream its events to completion:

```csharp
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;

var ws = new System.Net.WebSockets.ClientWebSocket();
await ws.ConnectAsync(new Uri("wss://runtime.example.com/arcp"), CancellationToken.None);
var transport = new WebSocketTransport(ws);

await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "quickstart", Version = "1.0.0" },
    Token = Environment.GetEnvironmentVariable("ARCP_TOKEN"),
});

var handle = await client.SubmitAsync(
    agent: "data-analyzer",
    input: new { dataset = "s3://example/sales.csv" });

_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
        Console.WriteLine($"[{ev.EventSeq}] {ev.Kind}");
});

var result = await handle.Result;
Console.WriteLine($"final: {result.FinalStatus}");
```

This is the whole shape of the SDK: open a session, submit work, consume an ordered event stream, get a terminal result or error. Everything below is detail on those four moves.

## Concepts

ARCP organizes everything around four concerns — **identity**, **durability**, **authority**, and **observability** — expressed through five core objects:

- **Session** — a connection between a client and a runtime. A session carries identity (a bearer token), negotiates a feature set in a `hello`/`welcome` handshake, and is *resumable*: if the transport drops, you reconnect with a resume token and the runtime replays buffered events. Jobs outlive the session that started them. See [§6](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md).
- **Job** — one unit of agent work submitted into a session. A job has an identity, an optional idempotency key, a resolved agent version, and a lifecycle that ends in exactly one terminal state: `success`, `error`, `cancelled`, or `timed_out`. See [§7](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md).
- **Event** — the ordered, session-scoped stream a job emits: logs, thoughts, tool calls and results, status, metrics, artifact references, progress, and streamed result chunks. Events carry strictly monotonic sequence numbers so the stream survives reconnects gap-free. See [§8](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md).
- **Lease** — the authority a job runs under, expressed as capability grants (`fs.read`, `fs.write`, `net.fetch`, `tool.call`, `agent.delegate`, `cost.budget`, `model.use`). The runtime enforces the lease at every operation boundary; a job can never act outside it. Leases may carry a budget and an expiry, and may be subset and handed to sub-agents via delegation. See [§9](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md).
- **Subscription** — read-only attachment to a job started elsewhere (e.g. a dashboard watching a job a CLI submitted). A subscriber observes the live event stream but cannot cancel or mutate the job. Distinct from *resume*, which continues the original session and carries cancel authority. See [§7.6](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md).

The SDK models each of these as first-class objects; the rest of this README shows how.

## Guides

### Sessions and resume

Open a session, negotiate features, and reconnect transparently after a transport drop using the resume token — jobs keep running server-side while you're gone.

```csharp
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;

await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "resumable", Version = "1.0.0" },
    Token = Environment.GetEnvironmentVariable("ARCP_TOKEN"),
});

var sessionId = client.SessionId;
var resumeToken = client.ResumeToken;
var effective = client.EffectiveFeatures; // intersection of client/runtime hello.features

// ... transport drops; track the last seq your reader observed ...
var lastSeq = client.LastReceivedSeq;

// Capture `sessionId`, `resumeToken`, and `lastSeq` to hand to a fresh connection
// when reconnect logic re-establishes the transport (spec §6.3).
```

### Submitting jobs

Submit a job with an agent (optionally version-pinned as `name@version`), an input, and an optional lease request, idempotency key, and runtime limit.

```csharp
using Arcp.Core.Leases;

var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.NetFetch] = new[] { "s3://reports/**" },
});

var handle = await client.SubmitAsync(
    agent: "weekly-report@2.1.0",
    input: new { week = "2026-W19" },
    leaseRequest: lease,
    leaseConstraints: new LeaseConstraints { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) },
    idempotencyKey: "weekly-report-2026-W19",
    maxRuntimeSec: 300);

Console.WriteLine($"job_id = {handle.JobId}");
Console.WriteLine($"effective lease = {handle.Lease}");
Console.WriteLine($"resolved agent = {handle.Agent}");
```

### Consuming events

Iterate the ordered event stream — `log`, `thought`, `tool_call`, `tool_result`, `status`, `metric`, `artifact_ref`, `progress`, `result_chunk` — and optionally acknowledge progress so the runtime can release buffered events early.

```csharp
using Arcp.Core.Messages;

await foreach (var ev in handle.Events())
{
    switch (ev.Kind)
    {
        case EventKinds.Log:
            Console.WriteLine($"log: {ev.Body}");
            break;
        case EventKinds.ToolCall:
            Console.WriteLine($"→ tool: {ev.Body}");
            break;
        case EventKinds.Metric:
            Console.WriteLine($"metric: {ev.Body}");
            break;
        case EventKinds.Progress:
            Console.WriteLine($"progress: {ev.Body}");
            break;
    }

    // Coalesce acks so the runtime can release buffered events (spec §6.5).
    if (ev.EventSeq % 32 == 0)
        await client.AckAsync(ev.EventSeq);
}
```

### Leases and budgets

Request capabilities, a budget, and an expiry; read budget-remaining metrics as they arrive; handle the runtime's enforcement decisions.

```csharp
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Core.Messages;

var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.ToolCall]   = new[] { "search.*", "fetch.*" },
    [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
});

var handle = await client.SubmitAsync(
    agent: "web-research",
    input: new { iterations = 8, perCallUSD = 0.3 },
    leaseRequest: lease,
    leaseConstraints: new LeaseConstraints { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });

Console.WriteLine($"initial budget: {string.Join(",", handle.Budget!)}");

_ = Task.Run(async () =>
{
    await foreach (var ev in handle.Events())
    {
        if (ev.Kind != EventKinds.Metric) continue;
        var metric = ev.BodyAs<MetricBody>();
        if (metric?.Name == "cost.budget.remaining")
            Console.WriteLine($"budget remaining: {metric.Value} {metric.Unit}");
    }
});

var outcome = await handle.Result;
if (!outcome.Success)
{
    // BUDGET_EXHAUSTED and LEASE_EXPIRED are never retryable.
    outcome.EnsureSuccess();
}
```

### Subscribing to jobs

Attach read-only to a job submitted elsewhere and observe its live stream (with optional history replay) without cancel authority.

```csharp
using Arcp.Core.Ids;
using Arcp.Core.Messages;

var listing = await client.ListJobsAsync(
    filter: new JobListFilter { Status = new[] { "running" } });

var jobId = JobId.Parse(listing.Jobs[0].JobId, null);
var sub = await client.SubscribeAsync(jobId, history: true);
var ack = sub.Acknowledged.Result;
Console.WriteLine($"subscribed from seq={ack.SubscribedFrom} replayed={ack.Replayed}");

await foreach (var ev in sub.Events())
    Console.WriteLine($"[seq={ev.EventSeq}] {ev.Kind}");

await sub.UnsubscribeAsync();
```

### Error handling

Catch the typed error taxonomy and respect the `retryable` flag — `LEASE_EXPIRED` and `BUDGET_EXHAUSTED` are never retryable; a naive retry fails identically.

```csharp
using Arcp.Core.Errors;

try
{
    var handle = await client.SubmitAsync("flaky", input: new { });
    var outcome = await handle.Result;
    outcome.EnsureSuccess();
}
catch (ArcpException ex)
{
    if (ex.Code is ErrorCode.LeaseExpired or ErrorCode.BudgetExhausted)
    {
        throw; // resubmit with a fresh lease / budget instead
    }
    if (ex.Retryable)
    {
        // safe to retry with backoff (e.g. INTERNAL_ERROR, TIMEOUT, HEARTBEAT_LOST, AGENT_NOT_AVAILABLE)
    }
    throw;
}
```

## Feature support

ARCP features this SDK negotiates during the `hello`/`welcome` handshake:

| Feature flag | Status |
|---|---|
| `heartbeat` | Supported |
| `ack` | Supported |
| `list_jobs` | Supported |
| `subscribe` | Supported |
| `lease_expires_at` | Supported |
| `cost.budget` | Supported |
| `model.use` | Supported |
| `provisioned_credentials` | Supported |
| `progress` | Supported |
| `result_chunk` | Supported |
| `agent_versions` | Supported |

## Transport

ARCP is transport-agnostic. This SDK ships a `WebSocketTransport` (default), a `StdioTransport` for in-process child runtimes, and a `MemoryTransport` for tests and same-process workers. WebSocket is the default for networked runtimes; stdio is used for in-process child runtimes. Select one by constructing the corresponding transport (`new WebSocketTransport(socket)`, `new StdioTransport(input, output)`, `MemoryTransport.Pair()`) and passing it to `ArcpClient.ConnectAsync(transport, options)`; host integrations under `Arcp.AspNetCore` attach the WebSocket upgrade to a Kestrel endpoint via `IEndpointRouteBuilder.MapArcp(server)`.

## API reference

Full API reference — every type, method, and event payload — is in [`docs/`](docs/).

## Versioning and compatibility

This SDK speaks **ARCP v1.1 (draft)**. The SDK follows semantic versioning independently of the protocol; the protocol version it negotiates is shown above and in `session.hello`. A runtime advertising a different ARCP MAJOR is not guaranteed compatible. Feature mismatches degrade gracefully: the effective feature set is the intersection of what the client and runtime advertise, and the SDK will not use a feature outside it.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Protocol questions and proposed changes belong in the [spec repository](https://github.com/agentruntimecontrolprotocol/spec); SDK bugs and feature requests belong here.

## License

Apache-2.0 — see [`LICENSE`](LICENSE).
