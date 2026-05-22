# Arcp

`Arcp` is the umbrella meta-package for the ARCP C# SDK. It re-exports all
public types from the core, client, and runtime projects so that most
applications need only one `<PackageReference>`.

```sh
dotnet add package Arcp
```

## What it bundles

| Included project   | Purpose                                               |
| ------------------ | ----------------------------------------------------- |
| `Arcp.Core`        | Wire primitives — envelopes, leases, error codes.     |
| `Arcp.Client`      | `ArcpClient` — submit jobs, observe events.           |
| `Arcp.Runtime`     | `ArcpServer` — accept sessions, run agents.           |

Optional add-ons are **not** bundled and must be referenced explicitly:

| Package            | When to add                                           |
| ------------------ | ----------------------------------------------------- |
| `Arcp.AspNetCore`  | Kestrel / ASP.NET Core hosting (`MapArcp`).           |
| `Arcp.Otel`        | OpenTelemetry transport instrumentation.              |
| `Arcp.Hosting`     | `IHostedService` + `IHostApplicationLifetime` wiring. |
| `Arcp.Cli`         | `arcp` CLI tool — serve and submit from a terminal.   |

## Typical project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <!-- Core + Client + Runtime in one reference -->
    <PackageReference Include="Arcp" Version="1.1.*" />
    <!-- ASP.NET Core host -->
    <PackageReference Include="Arcp.AspNetCore" Version="1.1.*" />
    <!-- OTel instrumentation -->
    <PackageReference Include="Arcp.Otel" Version="1.1.*" />
  </ItemGroup>
</Project>
```

## Related

- [Getting started](../getting-started.md) — install and first run.
- [Architecture](../architecture.md) — project dependency graph.
- [Arcp.Core](./Arcp.Core.md) — wire primitives detail.
- [Arcp.Client](./Arcp.Client.md) — client API reference.
- [Arcp.Runtime](./Arcp.Runtime.md) — server/runtime API reference.
