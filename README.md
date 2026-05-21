# ARCP — Agent Runtime Control Protocol (C# / .NET reference)

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](#)
[![ARCP](https://img.shields.io/badge/arcp-v1.1-orange.svg)](../spec/docs/draft-arcp-1.1.md)

Reference C# / .NET 10 implementation of ARCP v1.1, the Agent Runtime Control Protocol — a transport-agnostic wire protocol for submitting, observing, and controlling long-running AI agent jobs.

## Install

```sh
dotnet add package Arcp
```

| Package           | Purpose                                                                    |
| ----------------- | -------------------------------------------------------------------------- |
| `Arcp`            | Umbrella meta-package; pulls Core + Client + Runtime.                       |
| `Arcp.Core`       | Wire primitives — envelopes, messages, errors, IDs, transports, event log. |
| `Arcp.Client`     | `ArcpClient`, `JobHandle`, `JobEvent`.                                     |
| `Arcp.Runtime`    | `ArcpServer`, `JobManager`, `LeaseManager`, `SessionState`.                |
| `Arcp.AspNetCore` | `IEndpointRouteBuilder.MapArcp("/arcp")` over Kestrel.                      |
| `Arcp.Otel`       | `ITransport.WithTracing()` — W3C trace propagation, ARCP span attributes.   |
| `Arcp.Hosting`    | `IServiceCollection.AddArcpRuntime()` for non-ASP.NET workers.              |
| `Arcp.Cli`        | `arcp` executable — `serve`, `submit`, `version`.                          |

## 20-line quickstart

```csharp
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "demo-runtime", Version = "1.0.0" },
});
server.RegisterAgent("echo", async (ctx, ct) =>
{
    await ctx.LogAsync("info", "received", ct);
    return ctx.Input;
});

var (clientTransport, serverTransport) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverTransport);

await using var client = await ArcpClient.ConnectAsync(clientTransport, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "demo-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("echo", new { hi = 1 });
var result = await handle.Result;
// result.Success == true; result.Result.FinalStatus == "success"
```

## v1.1 feature surface

Every feature ships behind a feature flag negotiated in `session.hello` / `session.welcome` (spec §6.2). The C# SDK advertises all of them by default:

| Flag                 | Spec   | Surface                                                                                        |
| -------------------- | ------ | ---------------------------------------------------------------------------------------------- |
| `heartbeat`          | §6.4   | `PeriodicTimer` ping/pong; not counted in `event_seq`.                                          |
| `ack`                | §6.5   | `ArcpClient.AckAsync(long)`; runtime emits `status{phase:"back_pressure"}` when lag is high.   |
| `list_jobs`          | §6.6   | `ArcpClient.ListJobsAsync()` returns paginated `JobListEntry`s.                                |
| `subscribe`          | §7.6   | `ArcpClient.SubscribeAsync(jobId)` returns a `JobSubscription` with `IAsyncEnumerable<JobEvent>`. |
| `agent_versions`     | §7.5   | `AgentRef.Parse("name@version")`; `RegisterAgentVersion()`; `AGENT_VERSION_NOT_AVAILABLE`.     |
| `progress`           | §8.2.1 | `JobContext.ProgressAsync(current, total?, units?, message?)`.                                 |
| `result_chunk`       | §8.4   | `JobContext.BeginResultStream()` + `WriteChunkAsync`; `JobHandle.Chunks()`.                    |
| `lease_expires_at`   | §9.5   | `LeaseConstraints { ExpiresAt = DateTimeOffset }`; watchdog emits `LEASE_EXPIRED`.             |
| `cost.budget`        | §9.6   | `BudgetAmount("USD:5.00")` patterns; `cost.*` metrics decrement the ledger; `BUDGET_EXHAUSTED`.|

## Repository layout

```
src/
  Arcp.Core/           wire types, IDs, envelopes, transports, event log
  Arcp.Client/         ArcpClient + JobHandle
  Arcp.Runtime/        ArcpServer + JobManager + LeaseManager + SessionState
  Arcp/                umbrella package
  Arcp.AspNetCore/     IEndpointRouteBuilder.MapArcp("/arcp")
  Arcp.Otel/           ITransport.WithTracing()
  Arcp.Hosting/        IServiceCollection.AddArcpRuntime()
  Arcp.Cli/            arcp serve / submit
samples/               20 runnable demos covering v1.0 core + v1.1 features
tests/
  Arcp.UnitTests/        envelope, parsers, event log
  Arcp.IntegrationTests/ end-to-end flows over MemoryTransport
  Arcp.ConformanceTests/ one [Fact] per spec § requirement
  Arcp.AspNetCore.Tests/ loopback Kestrel + ClientWebSocket
docs/                  narrative documentation
docs/diagrams/         6 Graphviz pairs (light/dark)
planning/v1.1/         design plan
```

## Run the samples

```sh
dotnet run --project samples/SubmitAndStream
dotnet run --project samples/Progress
dotnet run --project samples/ResultChunk
dotnet run --project samples/Heartbeat
dotnet run --project samples/CostBudget
dotnet run --project samples/AspNetCore   # listens on http://127.0.0.1:5519
```

## Conformance

See [`CONFORMANCE.md`](./CONFORMANCE.md) — one row per spec § with a pointer to the test that demonstrates it.

## Development

```sh
dotnet restore
dotnet build
dotnet test
```

Spec text lives in [the ARCP spec](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md). The C#-specific plan
