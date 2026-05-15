# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and the project follows semantic versioning.

## [1.0.0] - 2026-05-14

Initial release. Reference implementation of ARCP v1.1.

### Added

- `Arcp.Core` — envelope, message records, error taxonomy (15 codes), IDs, `MemoryTransport`/`WebSocketTransport`/`StdioTransport`, `EventLog`, capability negotiation, bearer auth, `ActivitySource` diagnostics.
- `Arcp.Client` — `ArcpClient` with hello/welcome, job submission, subscription, list_jobs, ack, and result streaming.
- `Arcp.Runtime` — `ArcpServer`, `JobManager`, `LeaseManager`, `SessionState`, `SubscriptionManager`, `BudgetLedger`, heartbeat watchdog, lease-expiry watchdog.
- `Arcp.AspNetCore` — `IEndpointRouteBuilder.MapArcp("/arcp")` over Kestrel WebSockets.
- `Arcp.Otel` — `ITransport.WithTracing()` with W3C `traceparent` propagation via the `x-vendor.opentelemetry.tracecontext` envelope extension.
- `Arcp.Hosting` — `IServiceCollection.AddArcpRuntime()` for non-ASP.NET worker processes.
- `Arcp.Cli` — `arcp serve` / `arcp submit` / `arcp version`.
- 20 runnable samples covering every v1.0 + v1.1 feature.
- 41 tests across UnitTests, IntegrationTests, ConformanceTests, and AspNetCore.Tests.
