# ARCP C# SDK documentation

Reference docs for the [ARCP](https://github.com/agentruntimecontrolprotocol/spec/blob/main/docs/draft-arcp-1.1.md) C# / .NET 10 SDK. The [top-level README](../README.md) is the front door — these pages go deeper into each subsystem.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./diagrams/arcp-projects-dark.svg">
  <img alt="ARCP C# SDK project graph" src="./diagrams/arcp-projects-light.svg">
</picture>

## Start here

- [Getting started](./getting-started.md) — install, build a runtime + client, run the example.
- [Architecture](./architecture.md) — how the eight NuGet projects fit together.
- [Transports](./transports.md) — WebSocket, stdio, in-memory; when to pick each.
- [CLI](./cli.md) — the `arcp` executable shipped by `Arcp.Cli`.

## Guides (one per spec section)

| Page                                               | Spec |
| -------------------------------------------------- | ---- |
| [Sessions](./guides/sessions.md)                   | §6   |
| [Resume](./guides/resume.md)                       | §6.3 |
| [Authentication](./guides/auth.md)                 | §6.1 |
| [Jobs](./guides/jobs.md)                           | §7   |
| [Job events](./guides/job-events.md)               | §8   |
| [Leases](./guides/leases.md)                       | §9   |
| [Delegation](./guides/delegation.md)               | §10  |
| [Observability](./guides/observability.md)         | §11  |
| [Errors](./guides/errors.md)                       | §12  |
| [Vendor extensions](./guides/vendor-extensions.md) | §15  |

## Projects

| Project              | Page                                                      |
| -------------------- | --------------------------------------------------------- |
| `Arcp`               | [projects/Arcp](./projects/Arcp.md)                       |
| `Arcp.Core`          | [projects/Arcp.Core](./projects/Arcp.Core.md)             |
| `Arcp.Client`        | [projects/Arcp.Client](./projects/Arcp.Client.md)         |
| `Arcp.Runtime`       | [projects/Arcp.Runtime](./projects/Arcp.Runtime.md)       |
| `Arcp.Hosting`       | [projects/Arcp.Hosting](./projects/Arcp.Hosting.md)       |
| `Arcp.AspNetCore`    | [projects/Arcp.AspNetCore](./projects/Arcp.AspNetCore.md) |
| `Arcp.Otel`          | [projects/Arcp.Otel](./projects/Arcp.Otel.md)             |
| `Arcp.Cli`           | [projects/Arcp.Cli](./projects/Arcp.Cli.md)               |

## Reference

- [Recipes](./recipes.md) — copy-paste solutions to common problems.
- [Conformance](./conformance.md) — spec coverage by section.
- [Troubleshooting](./troubleshooting.md) — error codes and fixes.
- [Style guide](./style-guide.md) — idiomatic C# conventions for this codebase.

## Diagrams

The hero diagram above is generated from Graphviz. The source files live in [`./diagrams/`](./diagrams/) — light/dark variants render through GitHub's `<picture>` element with `prefers-color-scheme`.
