# 08 — Docs Site & README

Goal: a `docs/` tree the shared docs site can ingest as plain Markdown,
a generated API reference produced separately by DocFX, XML doc comments
on every public symbol in the five shipping projects, and a README
that builds with `dotnet build` from the snippet alone. Citations:
spec § from `../spec/docs/draft-arcp-02.1.md`, TS paths under
`../typescript-sdk/`, and C# paths under
`/Users/nficano/code/arpc/csharp-sdk/`.

Dependency note: Phase 4 (`04-architecture.md`) and Phase 6
(`06-examples.md`) are being authored in parallel. Where this plan
names a type (`ArcpClient`, `ArcpRuntime`, `JobHandle`, etc.) or a
sample directory, treat the name as a **placeholder pinned to Phase 4 §
"Public API sketch" and Phase 6's sample table**; if Phase 4 lands a
different identifier (e.g. `ArcpServer` over `ArcpRuntime`) the docs
filenames here do not change, only the symbol references inside them.

## 1. `docs/` tree

```
docs/
  00-overview.md
  01-quickstart.md
  02-architecture.md
  03-client.md
  04-runtime.md
  05-transports.md
  06-sessions.md
  07-jobs.md
  08-events.md
  09-leases.md
  10-delegation.md
  11-tracing.md
  12-errors.md
  13-versioning.md
  14-subscriptions.md
  15-budget.md
  16-streaming-results.md
  17-middleware-aspnetcore.md
  18-middleware-otel.md
  diagrams/                 # owned by Phase 9
```

File-by-file justification, each pinned to spec § and at least one
upstream TS section in `../typescript-sdk/README.md`:

| File                          | Spec §           | Why this file exists                                                                                              |
| ----------------------------- | ---------------- | ----------------------------------------------------------------------------------------------------------------- |
| `00-overview.md`              | §1–§3            | One-screen positioning: what ARCP is, what v1.1 added, who this SDK is for. Mirror of TS README intro.            |
| `01-quickstart.md`            | §6, §7           | Same 20-line snippet as `README.md` quickstart, expanded with running-on-WebSocket variant. Source of truth for cut-and-paste. |
| `02-architecture.md`          | §4–§12 overview  | Project graph (`Arcp.Core` ← `Arcp.Client`/`Arcp.Runtime` ← `Arcp.AspNetCore`/`Arcp.Otel` ← `Arcp`). Diagram from Phase 9 (a). |
| `03-client.md`                | §6.1, §7.1       | `ArcpClient` lifecycle, `connect` / `submit` / `await done` / `close`. Mirrors `@arcp/client`.                    |
| `04-runtime.md`               | §6.2, §7.3       | `ArcpRuntime` lifecycle, `RegisterAgent`, `Accept(ITransport)`. Mirrors `@arcp/runtime`.                          |
| `05-transports.md`            | §4.1, §4.2       | `WebSocketTransport`, `StdioTransport`, `MemoryTransport` (test). Cite `Transport/*Transport.cs` from audit §5.   |
| `06-sessions.md`              | §6.1–§6.3        | `session.hello` / `session.welcome` / `session.bye`, resume token rotation. Diagram (d) capability negotiation.   |
| `07-jobs.md`                  | §7.1–§7.4        | Submit, accept, cancel, terminal events. State machine diagram (c).                                               |
| `08-events.md`                | §8.1–§8.3        | One `job.event` envelope; eight reserved kinds plus `x-vendor.*`. Table identical in shape to TS README §"Job events". |
| `09-leases.md`                | §9.1–§9.4        | Immutable per-job; glob matching; canonicalization (§14). Reserved namespaces.                                    |
| `10-delegation.md`            | §10              | `delegate` event kind, subset validation, trace inheritance.                                                      |
| `11-tracing.md`               | §11              | `ActivitySource` integration; `arcp.session_id` / `arcp.job_id` / `arcp.agent` attributes. Pair with §13/§15 doc. |
| `12-errors.md`                | §12              | 15 canonical codes (12 v1.0 + 3 new). Map to `ArcpException` subclasses (Phase 4 §"Errors").                      |
| `13-versioning.md` *(NEW)*    | §7.5             | `AgentRef` grammar `name@version`, default resolution, `AGENT_VERSION_NOT_AVAILABLE`. Maps `agent_versions` feature flag. |
| `14-subscriptions.md` *(NEW)* | §7.6, §6.6       | `job.subscribe` / `job.subscribed` / `job.unsubscribe`; `session.list_jobs` / `session.jobs`. Authorization policy seam (`IJobAuthorizationPolicy`, audit §4 row §6.2). |
| `15-budget.md` *(NEW)*        | §9.6             | `cost.budget` capability, `BudgetAmount` parsing, per-currency counters, `BUDGET_EXHAUSTED`. Why `decimal` not `double` (audit §4 row §9.6). |
| `16-streaming-results.md` *(NEW)* | §8.4         | `result_chunk` event, terminal `job.result` carrying `result_id`/`result_size`/`summary?`, client-side `await foreach` over `JobHandle.Chunks(CT)`. |
| `17-middleware-aspnetcore.md` | §4.1             | `Arcp.AspNetCore` — `IEndpointRouteBuilder.MapArcp("/arcp")`, WebSocket upgrade attachment, DNS-rebind / Host check, `IOptions<ArcpOptions>`. From Phase 5 §1. |
| `18-middleware-otel.md`       | §11, §13         | `Arcp.Otel` — span per envelope, traceparent on connect, `arcp.lease.expires_at`, `arcp.budget.remaining` tags (delta §13). From Phase 5 §3. |
| `diagrams/`                   | n/a              | Graphviz sources + rendered SVGs; owned by Phase 9 per BOOTSTRAP.

The four files marked **NEW for v1.1** map 1:1 to the four new
capability-gated features in `01-spec-delta.md` §5, §6, §8, §10. They
exist as separate documents because each is opt-in (negotiated via
`session.hello.payload.capabilities.features` per delta §1) and a
reader landing from the shared docs site's spec cross-link needs a
single page per feature flag.

Files 17 and 18 are middleware-scoped rather than spec-scoped because
they describe the host adapter integration (`Arcp.AspNetCore`,
`Arcp.Otel`) and not the wire protocol. They reference Phase 5 for
implementation detail and the spec § for what they implement.

## 2. Frontmatter contract

Every doc file under `docs/` begins with this YAML block:

```yaml
---
title: <one-line, sentence case, no trailing period>
sdk: csharp
spec_sections: ["§6.2", "§6.4"]
order: 7
kind: guide | reference | overview | example
---
```

Field semantics:

- `title`: rendered as the page H1 by the shared docs site; the file
  itself MUST NOT also include a `#` H1 line. The site is responsible
  for site chrome.
- `sdk`: literal `csharp`. The shared docs site filters by SDK to
  build per-language navigation. The TS docs use `sdk: typescript`;
  Python (when added) will use `sdk: python`.
- `spec_sections`: YAML list of strings, each formatted exactly as the
  spec emits them — `§6.4`, `§8.2.1`. **List, not scalar**, because a
  single doc routinely covers multiple sections (e.g.
  `14-subscriptions.md` covers `§6.6` and `§7.6`), and a single
  section is referenced from multiple docs (`§12` appears in
  `12-errors.md`, `13-versioning.md`, `15-budget.md`, `16-streaming-results.md`).
  The docs site uses this list to render "as defined in §6.6, §7.6"
  cross-links back to the spec and to power a "docs by spec section"
  reverse index. A scalar would force one-to-one and we'd lose the
  many-to-many we actually have.
- `order`: integer matching the filename prefix. Authoritative for
  navigation order; if a reader's tooling sorts lexically and a future
  file is added between `10` and `11`, the `order` value lets it stay
  authoritative without renaming on disk.
- `kind`: one of four enum values. The docs site renders different
  chrome (e.g. a "reference" page gets a sticky symbol table; an
  "example" page gets a runnable badge).

Example frontmatter for `14-subscriptions.md`:

```yaml
---
title: Job subscriptions and listing
sdk: csharp
spec_sections: ["§6.6", "§7.6"]
order: 14
kind: guide
---
```

The frontmatter is a hard contract: the shared docs site fails its
build on missing or unrecognized fields. Reject PRs that omit it.

## 3. XML doc comments

Every public symbol in `Arcp.Core`, `Arcp.Client`, `Arcp.Runtime`,
`Arcp.AspNetCore`, `Arcp.Otel` carries XML doc comments. The
projects already set `GenerateDocumentationFile = true`
(`Directory.Build.props`, audit §3), so a missing comment surfaces as
warning **CS1591** which `TreatWarningsAsErrors = true` (same file)
already escalates to a build failure. No new MSBuild plumbing
needed.

Rules:

1. `<summary>` is **one sentence**, no trailing period inside the tag
   text (Roslyn appends one). It describes what the symbol **is**,
   not what it **does**. Example:
   - Good: `<summary>A typed handle to a submitted ARCP job.</summary>`
   - Bad:  `<summary>Submits and awaits an ARCP job.</summary>` —
     that's a method behavior, not a type identity.
2. `<remarks>` is the only place where spec § citations appear, in
   the form `(spec §6.4)`. The single-sentence `<summary>` MUST NOT
   carry citations because DocFX renders summaries in tooltips where
   `§` glyph breaks IntelliSense rendering on some Windows code
   pages.
3. `<inheritdoc/>` is used **only** on overrides whose base
   `<summary>` is identical in meaning. Do not use it on interface
   implementations where the implementation refines the contract
   (e.g. `WebSocketTransport.SendAsync` adds "frames are text per
   §4.1" to `ITransport.SendAsync`'s generic contract).
4. `<param>` and `<returns>` are required **only where they aren't
   trivially obvious**. The rule of thumb: if removing the tag would
   leave a reader uncertain, write it; if the tag would just
   restate the parameter name, omit it. Concretely: do not write
   `<param name="jobId">The job id.</param>` — that's noise.
   Do write `<param name="from">Resume from this event_seq, exclusive (spec §6.5).</param>`.
5. `<exception cref="…">` is required for every documented thrown
   exception path. The 15 `ArcpException` subclasses (Phase 4
   §Errors) each get cited on the methods that surface them — e.g.
   `ArcpClient.SubmitAsync` documents `cref="AgentVersionNotAvailableException"`
   when an `AgentRef` carries a pinned version that the runtime
   doesn't have (delta §5).
6. `<see cref="T:…">` for cross-type links uses the **fully qualified
   metadata-name form** (`T:Arcp.Core.Envelope`) for any type that
   appears in DocFX's cross-link index. See risks below for the
   generic-parameter escape.
7. Async methods: `<returns>A task that completes when …</returns>`,
   not `<returns>A Task.</returns>`. The reader already knows it's a
   Task; tell them what the completion means.
8. `CancellationToken` parameters: standard text
   `<param name="cancellationToken">Token to cancel the operation. Required to be the last parameter on every public async method (BOOTSTRAP).</param>` —
   write it once per public method; it's not noise because BCL
   IntelliSense relies on it.

Naming: comments use the SDK's spelling ("ARCP", not "Arcp"), which
matches the protocol spec and the README banner. The CLR identifier
`Arcp` only appears inside `<see cref>` and `<paramref>`.

## 4. API reference generation — DocFX

**Pick: DocFX.** Three reasons specific to this SDK:

1. **.NET-native**: DocFX consumes the `bin/<TFM>/Arcp.Core.xml`
   etc. files emitted by `GenerateDocumentationFile = true` directly
   and resolves `<see cref>` against the assembly metadata. No
   separate parsing step. Sandcastle does the same but is
   community-maintained with sporadic releases; DocFX is Apache-licensed
   and actively maintained (NuGet package `docfx`, current at v2.78
   as of 2026-05).
2. **GitHub Pages without infra**: `docfx build` emits static HTML
   to `_site/`; a single GitHub Actions step (`peaceiris/actions-gh-pages`)
   publishes it. No Jekyll, no server-side rendering, no docs host.
   The shared docs site for plain Markdown is unaffected.
3. **Cross-link to the shared docs site**: DocFX `xref` maps emit a
   public `xrefmap.yml` we can ship alongside the generated site. The
   shared docs site consumes that map to turn `T:Arcp.Core.Envelope`
   references inside `docs/*.md` (when an author writes
   `[`Envelope`](xref:Arcp.Core.Envelope)`) into deep links into the
   generated reference. The reverse (DocFX linking from generated
   reference back into the shared docs site) is a single
   `_overwrite.md` per type — DocFX merges in extra prose without
   touching the assembly.

Rejected alternatives:

- **Sandcastle Help File Builder**: last release Aug 2024; community-maintained;
  HTML output is `.chm`-flavored.
- **Doxygen**: language-agnostic. Doesn't know about `<inheritdoc/>`
  semantics, `record` syntactic sugar, or `IAsyncEnumerable<T>`
  conventions. Output looks like 2010.
- **`dotnet/api-docs` (Microsoft Learn pipeline)**: that pipeline is
  for BCL and `dotnet/runtime`-tier projects; the ingestion contract
  isn't open to third-party libraries.

**Topology**: shared docs site renders `docs/*.md` as Markdown;
DocFX produces a separate static site at `/api/` (or a sibling
domain `api.arcp.dev/csharp/`) consuming the per-project XML docs.
Cross-links from `docs/*.md` to the API reference use `xref:` URLs
resolved against the published `xrefmap.yml`. Cross-links from the
API reference to `docs/*.md` use plain anchor URLs since the shared
docs site emits stable slugs from the filename.

**DocFX config skeleton** (`docfx.json` at repo root, plan-only — do
not write yet):

```jsonc
{
  "metadata": [{
    "src": [{ "files": ["src/**/*.csproj"] }],
    "dest": "_api"
  }],
  "build": {
    "content": [
      { "files": ["_api/**.yml", "_api/index.md"] }
    ],
    "xref": ["https://learn.microsoft.com/en-us/dotnet/.xrefmap.json"],
    "globalMetadata": { "_appTitle": "ARCP C# SDK API", "_enableSearch": true }
  }
}
```

The `xref` entry pulls in Microsoft's xrefmap so that references to
`T:System.IAsyncDisposable` resolve to learn.microsoft.com without
us hand-rolling links.

CI: a `docs/` workflow runs `dotnet tool restore` (installing the
`docfx` tool pinned in `dotnet-tools.json`), then `docfx build`, then
publishes `_site/` to GitHub Pages. Local: `dotnet docfx serve _site`.

## 5. README outline

`README.md` at `/Users/nficano/code/arpc/csharp-sdk/README.md`.
Current contents are a 5-line stub (audit §1). New layout:

### Banner

```
# ARCP — Agent Runtime Control Protocol (C# / .NET reference)

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0%20%7C%2010.0-512BD4.svg)](#)
[![ARCP](https://img.shields.io/badge/arcp-v1.1-orange.svg)](../spec/docs/draft-arcp-02.1.md)
[![NuGet](https://img.shields.io/nuget/v/Arcp.svg)](https://www.nuget.org/packages/Arcp/)
```

### One-line description

> Reference C# / .NET 9–10 implementation of ARCP v1.1, the Agent
> Runtime Control Protocol.

### Status banner

```
> Status: experimental. The wire format is stable (v1.1 additive over v1.0);
> the C# API surface is pre-1.0 and may change between 0.x releases.
```

The pick is **experimental**, not "production-ready" (banned word
anyway) and not "archived". Reasoning is anchored in audit §1: the
SDK currently does not produce the v1.0 wire format, so the first
shipping release (PR-1) is `0.2.0` against v1.0 ARCP; v1.1 features
ship `0.3.0–0.10.0`; only `1.0.0` matching ARCP v1.1 is the
not-experimental marker. See §7 below for the versioning rule.

### Install

```sh
dotnet add package Arcp
```

Followed by the packaging table (§5 below) so readers see which
package they actually want.

### 20-line quickstart

Builds with `dotnet build` from this snippet alone — single file
plus `<PackageReference Include="Arcp" />`. Names pinned to
Phase 4's "Public API sketch":

```csharp
using Arcp;
using Arcp.Client;
using Arcp.Runtime;

var token = "tok-demo";
await using var runtime = new ArcpRuntime(new ArcpRuntimeOptions
{
    Runtime = new("demo-runtime", "1.0.0"),
    Capabilities = new(Encodings: ["json"], Agents: ["echo"]),
    Bearer = new StaticBearerVerifier((token, new("demo"))),
});
runtime.RegisterAgent("echo", async (input, ctx, ct) =>
{
    await ctx.LogAsync("info", "received", ct).ConfigureAwait(false);
    return new { echoed = input };
});

var (clientTransport, serverTransport) = MemoryTransport.Pair();
runtime.Accept(serverTransport);

await using var client = new ArcpClient(new ArcpClientOptions
{
    Client = new("demo-client", "1.0.0"),
    AuthScheme = "bearer",
    Token = token,
});
await client.ConnectAsync(clientTransport).ConfigureAwait(false);
var handle = await client.SubmitAsync(new("echo", new { hi = 1 })).ConfigureAwait(false);
var result = await handle.Done.ConfigureAwait(false);
// result.FinalStatus == "success", result.Result == { echoed = { hi = 1 } }
```

This snippet must compile against the Phase 4 design. **Explicit
pre-merge check** (anti-slop §6): before merging Phase 8 with the
final README, copy the snippet into `samples/QuickstartSmoke/` and
run `dotnet build -c Release` plus `dotnet run --project samples/QuickstartSmoke`;
exit code 0 is the gate. Repeat for every code block in `docs/*.md`.

### Packaging table

| Package          | Purpose                                                                | Target frameworks |
| ---------------- | ---------------------------------------------------------------------- | ----------------- |
| `Arcp`           | Umbrella meta-package; re-exports `Arcp.Client`, `Arcp.Runtime`, ships `arcp` CLI. | `net9.0; net10.0` (CLI: `net10.0`) |
| `Arcp.Core`      | Shared primitives — envelopes, errors, messages, transports, event log, auth, session state. | `net9.0; net10.0` |
| `Arcp.Client`    | `ArcpClient` for talking to an ARCP runtime. Depends on `Arcp.Core`.   | `net9.0; net10.0` |
| `Arcp.Runtime`   | `ArcpRuntime`, `Job`, `JobContext`, lease helpers. Depends on `Arcp.Core`. | `net9.0; net10.0` |
| `Arcp.AspNetCore`| `IEndpointRouteBuilder.MapArcp("/arcp")`, WS upgrade attachment, `IOptions<ArcpOptions>`. | `net9.0; net10.0` |
| `Arcp.Otel`      | `ActivitySource` instrumentation; W3C traceparent on connect; spec §11/§13 attributes. | `net9.0; net10.0` |

Rows mirror the TS workspace map in `../typescript-sdk/README.md`
§Install, scoped to the C# project graph from Phase 4 (assumed —
verify when 04-architecture.md lands).

### TFM compat table

| TFM        | Library projects | Tools (`Arcp.Cli`) | Samples | Tests |
| ---------- | ---------------- | ------------------ | ------- | ----- |
| `net9.0`   | yes              | no                 | no      | no    |
| `net10.0`  | yes              | yes                | yes     | yes   |

Rationale (audit §2): the audit recommends multi-TFM (`net9.0;net10.0`)
for libraries so .NET 9 LTS consumers aren't excluded; `Guid.CreateVersion7()`
(spec §5 wire `id`) is `net9.0+` so the floor is safe. Tools, samples,
and tests stay on `net10.0` only — they're internal and don't ship.

### Links

- [`CONFORMANCE.md`](./CONFORMANCE.md) — spec § × `{Implemented,Partial,Missing}` table.
- [`PLAN.md`](./PLAN.md) — release roadmap (synthesized from `planning/v1.1/10-synthesis.md`).
- [`docs/`](./docs/) — narrative documentation (this plan's §1).
- [`CHANGELOG.md`](./CHANGELOG.md) — release notes (§8 below).
- [`samples/`](./samples/) — runnable C# samples (Phase 6).
- [Generated API reference](https://example.invalid/api/csharp/) — DocFX site (§4 above).

### Core concepts section

Single-page summaries of Envelopes, Sessions, Jobs, Events, Leases,
Delegation, Resume — each ≤8 lines, each ending with "See
`docs/NN-foo.md` for the full reference." Mirrors the structure of
`../typescript-sdk/README.md` lines 96–222 but compressed: the README
is an index, not a manual.

### Running the runtime

Two sub-sections:

1. **Programmatic** — `ArcpRuntime` over `WebSocketTransport`, ~12
   lines, mirrors TS README "Running the runtime" block.
2. **CLI** — `dotnet tool install -g Arcp.Cli` then
   `arcp serve --host 127.0.0.1 --port 7777 --token tok --principal me@example.com`,
   `arcp submit`, `arcp replay`. Three sub-commands matching TS CLI
   in `../typescript-sdk/packages/sdk/src/cli.ts` (referenced by TS
   README).

### Conformance

Bullet list of spec sections implemented, identical structure to TS
README §Conformance. Cross-link to `CONFORMANCE.md`.

### Examples

Three sub-tables (v1.0 core, v1.1 features, host integrations) —
filled from Phase 6's example matrix when available. Same structure
as TS README §Examples.

### Repository layout

```
src/
  Arcp.Core/                # primitives
  Arcp.Client/              # ArcpClient
  Arcp.Runtime/             # ArcpRuntime
  Arcp/                     # umbrella + CLI
  Arcp.AspNetCore/          # endpoint mapping middleware
  Arcp.Otel/                # OTel adapter
samples/                    # runnable per-feature samples (Phase 6)
tests/
  Arcp.UnitTests/
  Arcp.IntegrationTests/
  Arcp.ConformanceTests/    # keyed to CONFORMANCE.md (Phase 7)
docs/                       # narrative docs (this plan §1)
planning/                   # planning markdown (this directory)
```

This differs from today's `src/ARCP/` single-project layout (audit
§2) — Phase 4 owns the split.

### Development

```sh
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet tool run docfx build
```

## 6. Voice rules

Hard bans (BOOTSTRAP "Anti-slop guardrails", restated here):

- No emoji anywhere in `README.md`, `CHANGELOG.md`, or `docs/*.md`.
- No marketing words: `leverage`, `robust`, `scalable`, `performant`,
  `powerful`, `modern`, `enterprise-grade`, `production-ready`,
  `first-class`. A pre-commit grep against this set lives in
  `.github/workflows/docs-lint.yml`; offending PRs fail.
- No bullets that restate their heading.
- Every paragraph cites: spec §, a TS path (`../typescript-sdk/...`),
  a C# path (`/Users/nficano/code/arpc/csharp-sdk/...`), a named
  NuGet (`docfx`, `Microsoft.Extensions.Logging.Abstractions`,
  `Ulid`), or a named C# idiom (`record`, `JsonPolymorphic`,
  `IAsyncEnumerable<T>`, `CancellationToken`, `ConfigureAwait(false)`).
- Every code block in `README.md` and `docs/*.md` compiles against
  the Phase 4 design. **Pre-merge gate**: a CI step extracts fenced
  ` ```csharp ` blocks via a small Roslyn script, drops each into a
  scratch project referencing the in-repo `Arcp.*` projects, runs
  `dotnet build`, and fails the PR if any block fails to compile.
  This is non-negotiable — drift between docs and API is the failure
  mode that kills SDK docs.

Tone parity check: the TS README opens "Reference implementation of
ARCP v1.0 … a small wire protocol …" — terse, factual, names the
thing. Match that register. Reject any paragraph that survives the
"language swap" test (BOOTSTRAP anti-slop): if the sentence reads
identically with "TypeScript" swapped for "C#", it's not pulling its
weight.

## 7. Versioning rule for shipped packages

Pin to NuGet semver:

| Version | Scope                                                                       |
| ------- | --------------------------------------------------------------------------- |
| `0.1.0` | Current `src/ARCP` — frozen, last v0 release; mark deprecated on NuGet.     |
| `0.2.0` | PR-1 lands the **v1.0 wire re-keying** (audit §1 lead milestone): re-keyed `MessageType`s, 12-code `ErrorCode`, dropped envelopes (`stream.*`, lease envelopes, `Subscriptions/`). Nothing v1.1 yet. |
| `0.3.0` | v1.1 §6.2 capability negotiation + §6.4 heartbeat.                          |
| `0.4.0` | v1.1 §6.5 ack + backpressure.                                               |
| `0.5.0` | v1.1 §6.6 list_jobs + §7.6 subscribe (shared `IJobAuthorizationPolicy`).     |
| `0.6.0` | v1.1 §7.5 `agent_versions` + `AGENT_VERSION_NOT_AVAILABLE`.                 |
| `0.7.0` | v1.1 §8.2.1 progress + §8.4 result_chunk.                                   |
| `0.8.0` | v1.1 §9.5 lease_expires_at + `LEASE_EXPIRED` watchdog.                      |
| `0.9.0` | v1.1 §9.6 cost.budget + `BUDGET_EXHAUSTED`.                                 |
| `0.10.0`| `Arcp.AspNetCore` + `Arcp.Otel` middleware (Phase 5).                       |
| `1.0.0` | GA matching ARCP v1.1. Pin API surface.                                     |

Every minor in `0.x` is potentially breaking on the C# API surface
per NuGet semver convention for pre-1.0 (`0.MAJOR.MINOR`). Each
release calls out the spec § it lands and the `CONFORMANCE.md` rows
it flips from `Missing`/`Partial` to `Implemented`.

## 8. CHANGELOG contract

`CHANGELOG.md` is already in the repo. The v1.1 entry MUST call out
the v0→v1.0 re-keying (the `0.2.0` row) and the v1.1 additions
separately — they're different kinds of change:

```markdown
## 0.2.0 — Wire re-keying to ARCP v1.0

### Removed
- `Messages/Streaming/*`, `Messages/Subscriptions/*`, `Messages/Permissions/*`
  lease envelopes (audit §6 — not in ARCP v1.0/v1.1).
- Top-level envelope fields `Source`, `Target`, `IdempotencyKey`,
  `Priority`, `CausationId`, `StreamId`, `SubscriptionId` (spec §5.1).
- 9 of 21 `ErrorCode` members (audit §1 table row §12 — keep 12 codes).

### Changed
- Session handshake renamed: `session.open` → `session.hello`,
  `session.accepted` → `session.welcome` (spec §6.2).
- `idempotency_key` moves from envelope top level into
  `job.submit.payload` (spec §7.2).
- Each event kind is no longer its own envelope type; all event kinds
  fold into one `job.event` envelope discriminated on
  `payload.kind` (spec §8.1).

### Added
- `job.submit` message type (was missing — audit §1 row §7.1).

## 0.3.0 — v1.1 §6.2 capabilities + §6.4 heartbeat
...
```

Format: Keep a Changelog 1.1.0 (`https://keepachangelog.com/en/1.1.0/`),
sections `Added` / `Changed` / `Deprecated` / `Removed` / `Fixed` /
`Security`. Each entry cites spec § or audit § or both.

## 9. Risks

Concrete C# pitfalls that bite during docs production:

1. **DocFX `xref` and generic angle brackets**. DocFX's `xref`
   resolver for `T:Arcp.Core.Envelope` works fine, but
   `T:Arcp.Core.JobHandle<TResult>` written as `<see cref="JobHandle&lt;TResult&gt;"/>`
   silently fails to resolve and emits a broken anchor. Escape with
   the **`{T}` ECMA-372 syntax**:
   `<see cref="JobHandle{TResult}"/>`. The Roslyn compiler accepts
   both; DocFX accepts only the `{}` form for cref resolution on
   generic types. CI must `docfx build --warningsAsErrors` so a
   broken xref fails the build.
2. **`<inheritdoc/>` on `IAsyncEnumerable<T>`-returning methods**.
   When `ITransport.ReceiveAsync` returns `IAsyncEnumerable<Envelope>`
   and `WebSocketTransport` overrides it with refined cancellation
   semantics, naive `<inheritdoc/>` drops the refinement from
   IntelliSense. Rule: never `<inheritdoc/>` on a streaming surface;
   always re-state the disposal contract.
3. **`record` primary-constructor parameters**. C# 12+ `record` types
   accept doc comments on primary-constructor parameters via the
   `<param>` tag on the type declaration. DocFX renders these
   correctly; the Roslyn analyzer **does not** raise CS1591 if they're
   missing, so they slip past `TreatWarningsAsErrors`. Mitigation:
   custom Roslyn analyzer in `Arcp.Internal.Analyzers` (or
   `Meziantou.Analyzer` rule `MA0048`) that flags undocumented
   primary-constructor parameters on public records. Track as Phase 7
   tests dependency.
4. **`xrefmap.yml` collision with TS docs site**. The shared docs
   site already consumes a TS-generated xrefmap. The C# DocFX output
   must namespace its xref UIDs (DocFX does this by default —
   `Arcp.Core.Envelope` not `Envelope` — but `T:System.String` is
   shared). Verify the shared site's resolver prefers the C# map for
   `T:Arcp.*` and the TS map for `@arcp/*` paths. No collision
   expected, but verify with a smoke test before the first release.
5. **`README.md` snippet drift**. The 20-line quickstart MUST compile
   against the in-repo project. Without the CI gate in §6, the
   snippet rots within two PRs. The gate is mandatory, not
   aspirational.
6. **Public records auto-generate `<inheritdoc/>`-defeating members**.
   `record` synthesizes `EqualityContract`, `ToString`, `<Clone>$`,
   etc. Roslyn does not require docs on synthesized members and
   CS1591 doesn't fire, but DocFX **does** include them in the API
   reference, polluting the symbol table. Fix: per-project
   `docfx.json` `metadata.filter` excluding compiler-generated
   members (`memberLayout: SamePage` + `filter: filterConfig.yml`
   with `- exclude: { hasAttribute: { uid: System.Runtime.CompilerServices.CompilerGeneratedAttribute } }`).

## 10. Pre-merge checklist for docs PRs

A docs PR is mergeable only when:

- [ ] All new/changed `docs/*.md` files have valid frontmatter (§2).
- [ ] All new/changed code blocks in `README.md` and `docs/*.md`
      compile via the CI gate (§6).
- [ ] All new public symbols in `Arcp.{Core,Client,Runtime,AspNetCore,Otel}`
      have XML doc comments (CS1591 + `TreatWarningsAsErrors` enforces).
- [ ] `docfx build --warningsAsErrors` passes (§9 risk 1).
- [ ] `CHANGELOG.md` has an entry under the appropriate version.
- [ ] No banned words (§6). The CI grep catches this.
- [ ] `CONFORMANCE.md` rows touched by the PR flip status accurately
      and cite the same spec § as the docs page (§1).
