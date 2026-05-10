# ARCP — C# SDK

Reference C# / .NET 10 implementation of the
[Agent Runtime Control Protocol (ARCP) v1.0](RFC-0001-v2.md).

> Status: **v0.1 in development.** See [PLAN.md](PLAN.md) for scope and phase
> ordering, and [CONFORMANCE.md](CONFORMANCE.md) for the implemented vs.
> deferred surfaces.

## Quickstart

Prerequisites: .NET 10 SDK (10.0.x). Pinned via `global.json`.

```sh
git clone <repo-url>
cd csharp-sdk
dotnet build -c Release
dotnet test  -c Release
dotnet run   --project samples/01.MinimalSession
```

## Packages

- `ARCP` — protocol library (envelopes, runtime, client, transports, store).
- `ARCP.Cli` — `arcp` command-line tool (`serve`, `tail`, `send`, `replay`).

## Layout

| Path                          | Purpose                                       |
| ----------------------------- | --------------------------------------------- |
| `src/ARCP/`                   | Main library                                  |
| `src/ARCP.Cli/`               | Command-line tool                             |
| `tests/ARCP.UnitTests/`       | Per-component unit tests                      |
| `tests/ARCP.IntegrationTests/`| End-to-end protocol tests                     |
| `samples/`                    | Six runnable sample applications              |
| `RFC-0001-v2.md`              | The protocol specification (source of truth)  |
| `PLAN.md`                     | Implementation plan                           |
| `CONFORMANCE.md`              | Section-by-section conformance status         |

Phase 7 fills out the rest of this README with architecture diagrams and a
mapping from RFC sections to source paths.
