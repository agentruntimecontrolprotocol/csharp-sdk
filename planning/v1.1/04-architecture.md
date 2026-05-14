# 04 — Architecture & Idioms

Target: ARCP v1.1 layered on a v1.0-conformant substrate. Replaces the
internal `RFC-0001-v2` taxonomy that `src/ARCP/` currently carries (see
`02-current-audit.md` §1). Plan only — no `.cs`.

## 1. Solution / project layout

Four library projects mirroring TS `packages/{core,client,runtime,sdk}`
(`../typescript-sdk/packages/{core,client,runtime,sdk}/`), plus the
CLI and adapters. The split is deliberate: `Arcp.Core` is the wire
contract reference (`Envelope`, `MessageType`, `ErrorCode`, IDs);
`Arcp.Client` and `Arcp.Runtime` reference `Arcp.Core` and each other
not at all; the umbrella `Arcp` package re-exports the three for one-
line `dotnet add package Arcp`, matching `@arcp/sdk` in
`../typescript-sdk/packages/sdk/src/index.ts`.

```
ARCP.sln
├── src/
│   ├── Arcp.Core/                 # wire types, IDs, envelope JSON, store, errors
│   │   └── Arcp.Core.csproj
│   ├── Arcp.Client/               # ArcpClient, JobHandle, transport client glue
│   │   └── Arcp.Client.csproj     -> ref Arcp.Core
│   ├── Arcp.Runtime/              # ArcpServer, JobManager, LeaseManager, SessionState
│   │   └── Arcp.Runtime.csproj    -> ref Arcp.Core
│   ├── Arcp/                      # umbrella façade — no code, only [InternalsVisibleTo]+TypeForwards
│   │   └── Arcp.csproj            -> ref Arcp.Core, Arcp.Client, Arcp.Runtime
│   ├── Arcp.AspNetCore/           # WebSocket endpoint adapter (Phase 5)
│   │   └── Arcp.AspNetCore.csproj -> ref Arcp.Runtime
│   ├── Arcp.Otel/                 # ActivitySource attribute names (Phase 5)
│   │   └── Arcp.Otel.csproj       -> ref Arcp.Core
│   └── Arcp.Cli/                  # `arcp` exe; was src/ARCP.Cli
│       └── Arcp.Cli.csproj        -> ref Arcp
├── tests/
│   ├── Arcp.Core.Tests/           -> Arcp.Core
│   ├── Arcp.Client.Tests/         -> Arcp.Client (+ Arcp.Runtime in-proc via MemoryTransport)
│   ├── Arcp.Runtime.Tests/        -> Arcp.Runtime
│   └── Arcp.Conformance.Tests/    -> Arcp (full surface; one xUnit class per CONFORMANCE.md row)
└── samples/                        # one project per TS example (Phase 6)
```

Migration path from the current monolith `src/ARCP/`: this is a **hard
cut**, not a re-export shim. The current namespace `ARCP.*` (uppercase)
becomes `Arcp.Core.*` / `Arcp.Client.*` / `Arcp.Runtime.*` (Pascal-case
matching .NET conventions and the TS package names). A shim project
would freeze the v0 wire vocabulary into the package surface and the
audit (§1) is unambiguous that the v0 vocabulary is wrong — keeping it
behind type-forwards just guarantees its accidental reuse. The
`src/ARCP/` folder is renamed to `src/Arcp.Core/` in the same PR that
drops the v0 message taxonomy (`Messages/Streaming/*`,
`Messages/Subscriptions/*`, lease envelopes — see §8 below).

Why one `Arcp` umbrella, not five: TS uses `@arcp/sdk` as a re-export
of `@arcp/{core,client,runtime}`. The C# equivalent is a project with
zero source files and `<ProjectReference>`s plus `[assembly:
TypeForwardedTo(...)]` for the public-API surface, so `dotnet add
package Arcp` pulls everything and `using Arcp;` resolves to the same
types as `using Arcp.Client;`.

Why not collapse `Client` and `Runtime` into `Core`: a client-only
consumer pulling `Arcp.Runtime` would drag in `JobManager`,
`LeaseManager`, `SessionState`, and the heartbeat watchdog — code that
has no purpose on the consuming side and pulls a larger trimming
surface for `PublishAot`. The TS split (`packages/client/`,
`packages/runtime/`) has the same motive.

## 2. Target frameworks per project

| Project              | TFMs                | Why                                                                       |
| -------------------- | ------------------- | ------------------------------------------------------------------------- |
| `Arcp.Core`          | `net9.0;net10.0`    | wire types; on .NET 9 LTS for non-bleeding-edge consumers                 |
| `Arcp.Client`        | `net9.0;net10.0`    | same                                                                      |
| `Arcp.Runtime`       | `net9.0;net10.0`    | same                                                                      |
| `Arcp`               | `net9.0;net10.0`    | umbrella matches the floor of its refs                                    |
| `Arcp.AspNetCore`    | `net9.0;net10.0`    | Kestrel WebSockets and `IEndpointRouteBuilder` are stable on both         |
| `Arcp.Otel`          | `net9.0;net10.0`    | `System.Diagnostics.DiagnosticSource` is BCL                              |
| `Arcp.Cli`           | `net10.0`           | exe only — no need to ship two RIDs                                       |
| `tests/*`            | `net10.0`           | xUnit hosts on whatever is current; matrix-test against `net9.0` in CI    |
| `samples/*`          | `net10.0`           | same                                                                      |

`Guid.CreateVersion7()` (§5.1 envelope `id`, audit §8) is `net9.0+`, so
multi-TFM is safe. `System.Collections.Frozen` (`FrozenSet<string>`,
used for reserved kinds and capability namespaces) is .NET 8+ — present
in both. The current repo pins `net10.0` only (audit §2) — dropping to
multi-TFM is the only deliberate change in this section.

`global.json` keeps `rollForward: latestFeature, allowPrerelease: false`
on the .NET 10 SDK; the SDK builds both TFMs from one toolchain.

## 3. Type model

### 3.1. Envelope and payload polymorphism — one `Envelope`, not `Envelope<T>`

The spec (§5.1) puts the type discriminator on the envelope, not in the
payload: `{ "type": "job.submit", "payload": { ... } }`. TS encodes this
with a discriminated union on the envelope `type` field
(`../typescript-sdk/packages/core/src/envelope.ts`). The current C# code
already implements this via a custom `EnvelopeJsonConverter` against a
`MessageTypeRegistry` — keep that approach (audit §5).

`record Envelope` carries non-generic `MessageType Payload`. A generic
`Envelope<TPayload>` is rejected for two concrete reasons: (a) reading
a stream of envelopes from a transport yields `IAsyncEnumerable<Envelope>`
with mixed payload types — generic parameterization forces the consumer
to know the type before they read, which is backwards; (b) source-
generated `JsonSerializerContext` (see §3.3) cannot enumerate a closed
set of `Envelope<T>` instantiations without one entry per payload type,
which scales worse than one entry per `MessageType` subclass.

The `Envelope` record drops the v0 fields that are not in spec §5.1
(audit §6): `Source`, `Target`, `StreamId`, `SubscriptionId`,
`Priority`, `CausationId`, and the top-level `IdempotencyKey`. The
kept fields are: `Arcp` (`"1"`), `Id` (`MessageId`), `Type` (string),
`Timestamp` (`DateTimeOffset`), `SessionId?`, `JobId?`, `TraceId?`,
`SpanId?`, `ParentSpanId?`, `EventSeq?` (`long?`), `Payload`
(`MessageType`), `Extensions` (`IReadOnlyDictionary<string, JsonElement>`
for §5.1 unknown-field passthrough). `init`-only on every property;
`required` on the four mandatory ones.

### 3.2. `[JsonPolymorphic]` vs. custom converter — keep the converter

`[JsonPolymorphic]` + `[JsonDerivedType]` (`System.Text.Json` 8+) places
the discriminator **inside** the polymorphic object via
`TypeDiscriminatorPropertyName`. Spec §5.1 puts `type` at the envelope
level — `payload` itself has no `type` field. To make
`[JsonPolymorphic]` work on `MessageType` you have to either (a)
serialize the discriminator twice (once on the envelope, once on the
payload) and ignore one of them on read, or (b) hoist the property at
read time via `JsonTypeInfo.PolymorphismOptions.IgnoreUnrecognizedTypeDiscriminators`
+ a custom resolver that reads from the parent — the resolver path
is roughly the same code as the existing custom converter, with extra
contract-resolver glue.

Decision: keep `EnvelopeJsonConverter`. The audit (§5, "keep approach
(custom converter)") already calls this out. `[JsonPolymorphic]` is
the right tool when the discriminator is **on** the polymorphic type,
not next to it.

### 3.3. Source-generated `JsonSerializerContext`

One `JsonSerializerContext` per project — `ArcpCoreJsonContext` in
`Arcp.Core`, `ArcpClientJsonContext` in `Arcp.Client`,
`ArcpRuntimeJsonContext` in `Arcp.Runtime`. Each is marked
`[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy =
JsonKnownNamingPolicy.SnakeCaseLower)]` and lists the records it owns.
Generated contexts are combined at the `EnvelopeJson.Options`
construction site with `Default = JsonTypeInfoResolver.Combine(
ArcpCoreJsonContext.Default, ArcpClientJsonContext.Default,
ArcpRuntimeJsonContext.Default)`. This keeps each project AOT-friendly
in isolation (Phase 3 hard rule) and avoids the reflection fallback
that `JsonSerializer.Deserialize<T>` uses when the type is not in any
context.

### 3.4. Reserved-string sets

Reserved kinds (§8.2 — `log`, `thought`, `tool_call`, `tool_result`,
`status`, `metric`, `artifact_ref`, `delegate`, `progress`,
`result_chunk`), capability namespaces (§9.2 — `fs.read`, `fs.write`,
`net.fetch`, `tool.call`, `agent.delegate`, `cost.budget`), and feature
flags (§6.2 — `heartbeat`, `ack`, `list_jobs`, `subscribe`,
`lease_expires_at`, `cost.budget`, `progress`, `result_chunk`,
`agent_versions`) live as `static readonly FrozenSet<string>` from
`System.Collections.Frozen`. `FrozenSet<T>.Contains` outperforms
`HashSet<T>.Contains` for read-mostly sets of this size, and `Frozen`
signals "do not mutate" at the type level — a `HashSet` field could
still be `.Add`-ed by accident inside the runtime.

### 3.5. IDs

`Ids/*` from `src/ARCP/Ids/` stays (audit §5). Standardize on
`IParsable<TSelf>` and `ISpanParsable<TSelf>` for `MessageId`,
`SessionId`, `JobId`, `TraceId`, `SpanId`, `ArtifactId` so callers can
do `MessageId.Parse(jsonString.AsSpan())` zero-allocation when wired
through the `Utf8JsonReader`. `TraceId.TryFormat(Span<char>, out int)`
exists already; extend to every ID. `AgentRef` (§7.5,
`name@version`) is a new `readonly record struct` implementing
`IParsable<AgentRef>` with `name@version` grammar
(`[a-z0-9][a-z0-9._-]*` and `[a-zA-Z0-9.+_-]+`). `record struct` not
`class` avoids one heap alloc per submitted job since `agent` appears
on every `job.submit` (§7.1).

## 4. Async model

### 4.1. `Task` vs `ValueTask`

Rule: `ValueTask` on the hot inner loops where allocation matters
(transport send/receive, event emission from `JobContext`); `Task`
everywhere else (handshake, lifecycle, `DisposeAsync`). Concrete
examples:

- `ITransport.SendAsync(ReadOnlyMemory<byte>, CancellationToken)` →
  `ValueTask` — called once per envelope, sometimes synchronous when the
  transport is `MemoryTransport`.
- `JobContext.Log/Thought/ToolCall/...` → `ValueTask` — agents call these
  in inner loops.
- `ArcpClient.SubmitJobAsync` → `Task<JobHandle>` — called once per job.
- `ArcpClient.DisposeAsync` → `ValueTask` (required by
  `IAsyncDisposable`).

Pitfall to dodge: do not put `[AsyncMethodBuilder(typeof(...))]` on
public `ValueTask`-returning methods; `Microsoft.CodeAnalysis.NetAnalyzers`
rule `CA2012` ("Use ValueTasks correctly") covers the consumer side, but
producers that allow double-await (a `ValueTask` is single-consumption)
are not flagged — document on each `ValueTask`-returning method that it
must be awaited exactly once or converted via `.AsTask()`.

### 4.2. `IAsyncEnumerable<TEvent>` for streams

- `ArcpClient.SubscribeAsync(JobId, SubscribeOptions, CancellationToken)`
  → `IAsyncEnumerable<JobEvent>` (§7.6). Consumed via `await foreach`
  with `[EnumeratorCancellation] CancellationToken` plumbed through.
- `JobHandle.Chunks(CancellationToken)` →
  `IAsyncEnumerable<ResultChunk>` (§8.4). Re-assembles `result_chunk`
  events under one `result_id`; throws `InvalidOperationException` when
  the job's terminal `job.result` carried an inline `result` instead of
  `result_id` (§8.4: "MUST NOT mix inline result and `result_chunk`").
- `JobHandle.Events(CancellationToken)` → `IAsyncEnumerable<JobEvent>`
  for the job's whole event stream (not just chunks).
- `ITransport.ReceiveAsync(CancellationToken)` →
  `IAsyncEnumerable<WireFrame>`. `WireFrame` is a `record struct`
  wrapping `ReadOnlyMemory<byte>` + frame metadata; the transport owns
  the buffer for the duration of the yielded iteration step (see §6
  ITransport contract).

### 4.3. `CancellationToken` last argument

Every public async method ends with `CancellationToken ct = default`.
This is non-negotiable for the same reason TS uses `signal?: AbortSignal`
on every async method on `ARCPClient` — a non-cancellable handshake
breaks the §7.4 cancellation chain. `Meziantou.Analyzer` rule `MA0040`
("Forward the CancellationToken parameter") and `MA0079` ("Use a cancellable
overload") cover the enforcement side; Phase 3 must include Meziantou.

### 4.4. `System.Threading.Channels` for the ack / back-pressure seam

The runtime's outbound queue per session is a
`Channel<Envelope>.CreateBounded(new BoundedChannelOptions(capacity) {
SingleReader = true, SingleWriter = false, FullMode =
BoundedChannelFullMode.Wait })`. `Wait` is the right
`BoundedChannelFullMode` because §6.5 wants natural back-pressure —
`DropOldest` would silently lose events, `DropWrite` would silently
lose new ones, both violate §8.3's gap-free monotonic sequence
requirement. The producer side blocks on `WriteAsync` when full, which
propagates back to the agent's `JobContext.LogAsync` call site as a
suspended `ValueTask`. Lag = `highWatermark - lastAck`; when lag
crosses the configured threshold (default 1000, matching the TS
default in `../typescript-sdk/packages/runtime/src/types.ts`
`backPressureThreshold`), emit `status { phase: "back_pressure" }` (§13.2).

Inbound (transport → session dispatcher) uses an unbounded channel —
spec §6.5 puts back-pressure on the outbound (runtime → client) side
only.

### 4.5. `ConfigureAwait(false)` enforcement

`ConfigureAwait(false)` on every awaitable in every library project
(`Arcp.Core`, `Arcp.Client`, `Arcp.Runtime`, `Arcp.AspNetCore`,
`Arcp.Otel`). Not in `Arcp.Cli` (exe, no `SynchronizationContext` to
avoid). Mechanical enforcement: `Meziantou.Analyzer` rule `MA0004`
("Use Task.ConfigureAwait(false)") as `error` in
`Directory.Build.props` library targets. Without `ConfigureAwait(false)`
a library awaited from a UI thread (`WPF`, `WinForms`,
`AvaloniaUI`) resumes the continuation on the captured context —
that's a deadlock farm under `.Result`/`.Wait()`.

## 5. Errors

`ArcpException` (`Errors/ArcpException.cs`, renamed from
`ARCPException.cs`) is the base. Spec §12 plus the three v1.1 additions
gives **15** canonical codes. The current enum (`Errors/ErrorCode.cs`,
21 members, mostly gRPC names like `InvalidArgument`, `NotFound`,
`AlreadyExists`, `ResourceExhausted`, `DeadlineExceeded` — audit §1
table row §12) must be **replaced**, not extended. The v0 names are
not on the spec wire.

```
public abstract class ArcpException : Exception
{
    public string Code { get; }       // exact spec string, e.g. "PERMISSION_DENIED"
    public bool   Retryable { get; }  // §12 + §15 IANA registry
    public string? Detail { get; }
}
```

Subclasses (one per code; sealed):

| C# subclass                       | `Code`                          | Retryable | New in v1.1? |
| --------------------------------- | ------------------------------- | --------- | ------------ |
| `PermissionDeniedException`       | `PERMISSION_DENIED`             | false     |              |
| `LeaseSubsetViolationException`   | `LEASE_SUBSET_VIOLATION`        | false     |              |
| `JobNotFoundException`            | `JOB_NOT_FOUND`                 | false     |              |
| `DuplicateKeyException`           | `DUPLICATE_KEY`                 | false     |              |
| `AgentNotAvailableException`      | `AGENT_NOT_AVAILABLE`           | true      |              |
| `CancelledException`              | `CANCELLED`                     | false     |              |
| `TimeoutException`                | `TIMEOUT`                       | true      |              |
| `ResumeWindowExpiredException`    | `RESUME_WINDOW_EXPIRED`         | false     |              |
| `HeartbeatLostException`          | `HEARTBEAT_LOST`                | true      |              |
| `InvalidRequestException`         | `INVALID_REQUEST`               | false     |              |
| `UnauthenticatedException`        | `UNAUTHENTICATED`               | false     |              |
| `InternalErrorException`          | `INTERNAL_ERROR`                | false     |              |
| `AgentVersionNotAvailableException` | `AGENT_VERSION_NOT_AVAILABLE` | false     | ✓            |
| `LeaseExpiredException`           | `LEASE_EXPIRED`                 | false     | ✓            |
| `BudgetExhaustedException`        | `BUDGET_EXHAUSTED`              | false     | ✓            |

The C# `System.TimeoutException` shadow is intentional — the spec name
is `TIMEOUT`. Place `Arcp.Core.TimeoutException` in `namespace
Arcp.Core.Errors`; consumers `using Arcp.Core.Errors;` get the ARCP
type unless they explicitly disambiguate. The current SDK already does
this for `ARCPException`; the rename to `ArcpException` follows the
PascalCase convention used by `Arcp.Core` namespace casing.

`Code` is a `string`, not the enum. Reasons: (a) downstream code that
catches `ArcpException` and switches on `Code` survives spec
additions without recompiling; (b) the spec defines codes as strings
on the wire (§12), and converting through an enum + `JsonStringEnumConverter`
loses the exact spelling on round-trip of unknown codes
(deployment-specific codes are namespaced, e.g.
`arcpx.acme.QUOTA_EXCEEDED` per §12 — they must round-trip).

## 6. Public API sketch

Pseudocode for shape only; no method bodies. All signatures imply
`CancellationToken ct = default` as the last parameter. `sealed` on
every implementation class; `internal` on every dispatcher.

### 6.1. `Arcp.Client.ArcpClient`

```
namespace Arcp.Client;

public sealed class ArcpClient : IAsyncDisposable
{
    public static Task<ArcpClient> ConnectAsync(
        Uri endpoint, ArcpClientOptions options, CancellationToken ct);

    public SessionId         SessionId         { get; }      // §5.1
    public Capabilities      EffectiveFeatures { get; }      // §6.2 intersection
    public RuntimeIdentity   Runtime           { get; }
    public IReadOnlyList<AgentInventoryEntry> Agents { get; } // §6.2 rich shape

    public Task<JobHandle>             SubmitJobAsync(SubmitOptions opts, CancellationToken ct);
    public IAsyncEnumerable<JobEvent>  SubscribeAsync(JobId id, SubscribeOptions opts, CancellationToken ct);
    public Task                        UnsubscribeAsync(JobId id, CancellationToken ct);
    public Task<JobListPage>           ListJobsAsync(JobListFilter? filter, string? cursor, int? limit, CancellationToken ct);
    public ValueTask                   AckAsync(long lastProcessedSeq, CancellationToken ct);
    public Task<ArcpClient>            ResumeAsync(string resumeToken, CancellationToken ct);
    public Task                        CancelJobAsync(JobId id, string? reason, CancellationToken ct);
    public ValueTask                   DisposeAsync();
}
```

`SubscribeAsync` returns `IAsyncEnumerable<JobEvent>` (§7.6) — the
`job.subscribed` snapshot is delivered as the first item, then
`job.event` frames stream. `AckAsync` is `ValueTask` because it's
called from the auto-ack timer.

### 6.2. `Arcp.Runtime.ArcpServer`

```
namespace Arcp.Runtime;

public sealed class ArcpServer : IAsyncDisposable
{
    public ArcpServer(ArcpServerOptions options);

    public void RegisterAgent(string name, IAgent handler);                       // §7.5 unversioned
    public void RegisterAgentVersion(string name, string version, IAgent handler);// §7.5 versioned
    public void SetDefaultAgentVersion(string name, string version);              // §6.2 default
    public void SetJobAuthorizationPolicy(IJobAuthorizationPolicy policy);        // §6.6 / §7.6

    public Task<SessionContext> AcceptAsync(ITransport transport, CancellationToken ct);
    public IAsyncEnumerable<SessionContext> AcceptAllAsync(IListener listener, CancellationToken ct);

    public ValueTask DisposeAsync();
}

public interface IJobAuthorizationPolicy
{
    bool AuthorizeJobAccess(Job job, string? principal); // §6.6, §7.6 — same-principal default
}
```

`Arcp.Runtime.ArcpRuntime` exists too — it's the in-process
multiplexer that holds `JobManager`, `LeaseManager`, `SessionState`
collection — but `ArcpServer` is the documented entry point (matches
TS `ARCPServer` in `../typescript-sdk/packages/runtime/src/server.ts`).
`ArcpRuntime` stays `internal`.

### 6.3. `Arcp.Core.ITransport`

```
namespace Arcp.Core.Transport;

public interface ITransport : IAsyncDisposable
{
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);
    IAsyncEnumerable<WireFrame> ReceiveAsync(CancellationToken ct);
}

public readonly record struct WireFrame(ReadOnlyMemory<byte> Bytes, FrameKind Kind);
```

`record struct` to avoid one alloc per frame. The current
`Transport/ITransport.cs` is close — re-key the namespace and
signatures.

### 6.4. `Arcp.Runtime.IAgent`

```
namespace Arcp.Runtime;

public interface IAgent
{
    Task HandleAsync(JobContext ctx, CancellationToken ct);
}
```

Streaming results from an `IAgent`: write through
`JobContext.WriteChunkAsync`, then return; the runtime emits the
terminating `job.result` carrying `result_id` (§8.4). Inline results:
return value from a synchronous helper `ctx.SetResultAsync(object)`;
mixing is rejected at runtime with `InvalidRequestException` (§8.4
"MUST NOT mix inline result and `result_chunk`").

### 6.5. `JobContext` (one method per reserved kind)

```
public sealed class JobContext
{
    public JobId   JobId       { get; }
    public SessionId SessionId { get; }
    public AgentRef Agent      { get; }    // §7.5 — resolved name@version or bare
    public Lease    Lease      { get; }    // §9.1
    public LeaseConstraints? LeaseConstraints { get; } // §9.5
    public IReadOnlyDictionary<string, decimal> Budget { get; } // §9.6
    public TraceId? TraceId    { get; }
    public CancellationToken Cancellation { get; }    // signals on job.cancel + lease expiry
    public ILogger  Logger     { get; }    // Microsoft.Extensions.Logging.Abstractions

    public ValueTask LogAsync(LogLevel level, string message, IReadOnlyDictionary<string, object?>? attrs, CancellationToken ct);
    public ValueTask ThoughtAsync(string text, CancellationToken ct);
    public ValueTask StatusAsync(string phase, string? message, CancellationToken ct);
    public ValueTask MetricAsync(MetricBody body, CancellationToken ct);
    public ValueTask ToolCallAsync(ToolCallBody body, CancellationToken ct);
    public ValueTask ToolResultAsync(ToolResultBody body, CancellationToken ct);
    public ValueTask ArtifactRefAsync(ArtifactRefBody body, CancellationToken ct);
    public ValueTask DelegateAsync(DelegateBody body, CancellationToken ct);     // §10
    public ValueTask ProgressAsync(long current, long? total, string? units, string? message, CancellationToken ct); // §8.2.1
    public ValueTask WriteChunkAsync(ReadOnlyMemory<byte> data, ChunkEncoding encoding, CancellationToken ct);       // §8.4
    public ValueTask EmitEventAsync(string kind, object body, CancellationToken ct);                                  // x-vendor.*
}
```

Every method returns `ValueTask` because they're hot — an agent
emitting `progress` per file across 10k files allocates 10k `Task`s
otherwise. `Budget` is `IReadOnlyDictionary<string, decimal>` (audit
§4 — `decimal` not `double` for money). `progress` body validation
(§8.2.1: `current ≥ 0`, `current ≤ total` if total present) runs in
the primary constructor of `ProgressBody`, not in
`ProgressAsync` — invalid bodies throw `InvalidRequestException`
before any allocation hits the channel.

### 6.6. `JobHandle`, `Job`, `Session`

```
namespace Arcp.Client;

public sealed class JobHandle : IAsyncDisposable
{
    public JobId              JobId             { get; }
    public AgentRef           Agent             { get; }       // §7.1 — echoed agent
    public Lease              Lease             { get; }
    public LeaseConstraints?  LeaseConstraints  { get; }       // §9.5
    public IReadOnlyDictionary<string, decimal>? Budget { get; } // §9.6
    public TraceId?           TraceId           { get; }

    public IAsyncEnumerable<JobEvent>   Events(CancellationToken ct);                  // all events
    public IAsyncEnumerable<ResultChunk> Chunks(CancellationToken ct);                 // §8.4
    public Task<JobResult>              Result { get; }                                // terminal payload

    public ValueTask DisposeAsync();  // unsubscribes; does NOT cancel the job (§7.6)
}
```

```
namespace Arcp.Runtime;

internal sealed class Job        // owns lifecycle, exposed via JobContext + JobManager
{ ... }

public sealed class SessionContext : IAsyncDisposable
{
    public SessionId          SessionId  { get; }
    public Capabilities       Effective  { get; }   // §6.2 intersection
    public string?            Principal  { get; }   // bearer token subject, §6.1
    public ValueTask DisposeAsync();
}
```

`JobHandle.DisposeAsync` releases the subscription only —
`CancelJobAsync` is the explicit cancel. This matches TS where the
`JobHandle` does not own cancel authority for subscribed jobs (§7.6:
"Subscription does NOT grant the subscriber authority to cancel").

## 7. Hard rules summary

- **Nullable reference types treated as errors.** Already on in
  `Directory.Build.props` (audit §3: `Nullable = enable`,
  `TreatWarningsAsErrors = true`). Keep both as `error`, not
  `warning`.
- **`ConfigureAwait(false)` on every awaitable in library code.**
  Enforced via `Meziantou.Analyzer` `MA0004`, as `error`. Test
  projects and `Arcp.Cli` excluded — those run on an exe sync context
  where the difference is moot.
- **`sealed` by default on classes.** `Microsoft.CodeAnalysis.NetAnalyzers`
  `CA1852` ("Seal internal types") as `error`; public types are
  manually `sealed` unless inheritance is part of the documented
  surface (only `ArcpException` and `MessageType` are unsealed).
- **`internal` for the impl seam; `public` only for the documented
  API surface.** `JobManager`, `LeaseManager`, `SessionState`,
  `EnvelopeReader`, `PendingRegistry`, the heartbeat watchdog,
  `SubscriptionManager` are all `internal sealed`. `[InternalsVisibleTo("Arcp.Runtime.Tests")]`
  on `Arcp.Runtime`.
- **`IDisposable` / `IAsyncDisposable` everywhere a resource is owned.**
  `ITransport` is `IAsyncDisposable` (sockets);
  `ArcpClient`/`ArcpServer` are `IAsyncDisposable` (own a transport
  and a heartbeat `PeriodicTimer`); `JobHandle` is `IAsyncDisposable`
  (owns a subscription cursor); `SessionContext` is `IAsyncDisposable`
  (owns the outbound `Channel<Envelope>`). Synchronous `IDisposable`
  is reserved for things with no async unwind — none currently.
- **No `Task.Result` / `Task.Wait()` / `.GetAwaiter().GetResult()` in
  library code.** Audit (§3) confirms today's code has none; lock it in
  via `Meziantou.Analyzer` `MA0042` and `MA0045`.

## 8. Audit "delete" list — v1.0/v1.1 replacements

Each row from `02-current-audit.md` §6 paired with its v1.0/v1.1 home.

| Deleted (v0)                                                                 | Replacement                                                                              |
| ---------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `Messages/Streaming/*` (`stream.open`, `stream.chunk`, `stream.close`, ...)  | `result_chunk` event kind on `job.event` (§8.4); no separate envelope. Inline LLM-token streaming is **deferred** (spec "Not in v1.1"). |
| `Messages/Subscriptions/*` (`subscribe`, `subscribe.event`, ...)             | `job.subscribe` / `job.subscribed` / `job.unsubscribe` envelopes (§7.6) plus session-scoped event_seq replay — different shape, fresh records. |
| `Messages/Permissions/*` lease envelopes (`lease.granted`/`.refresh`/...)    | Lease is a **field** on `job.accepted.payload.lease` (§7.1); no `lease.*` envelope exists. `LeaseManager` keeps the subsetting math. |
| `Messages/Control/Backpressure` envelope                                     | `status { phase: "back_pressure" }` event (§13.2 / §6.5).                                |
| `Messages/Control/{CheckpointCreate, CheckpointRestore}`                     | **Deferred** — not in v1.0 or v1.1.                                                      |
| `Messages/Execution/{WorkflowStart, WorkflowComplete}`                       | **Deferred** — no workflow concept in spec.                                              |
| `Messages/Execution/AgentHandoff`                                            | `delegate` event kind on parent's `job.event` (§10).                                     |
| `Messages/Execution/{JobSchedule, JobProgress, JobHeartbeat, JobCheckpoint, JobStarted, JobFailed, JobCompleted, JobCancelled}` | Three terminal envelopes only: `job.accepted`, `job.event`, `job.result` / `job.error` (§7.1, §8.1). Progress moves to the `progress` event kind (§8.2.1). |
| `Envelope/Priority.cs`                                                       | **Deferred** — "priority & scheduling hints" is "Not in v1.1".                           |
| Top-level envelope fields `Source`, `Target`, `IdempotencyKey`, `Priority`, `CausationId`, `StreamId`, `SubscriptionId` | Gone from envelope. `idempotency_key` moves to `job.submit.payload.idempotency_key` (§7.2). |
| `samples/{Handoff, MCP, HumanInput, PermissionChallenge}`                    | Removed from `samples/` (Phase 6 rewrites).                                              |
| `Errors/ErrorCode.cs` (21 gRPC-flavored members)                             | Replaced by the 15-string list in §5 above; `string Code` not enum on the exception base. |
| `Capabilities` boolean grid (`Streaming`, `DurableJobs`, `Checkpoints`, ...) | `Capabilities { Encodings: string[], Features: string[], Agents: AgentInventoryEntry[] }` per §6.2. `FeatureSet.Intersect` for the v1.1 negotiation. |
