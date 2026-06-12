# Architecture

## Project graph

| Project           | Role |
| ----------------- | ---- |
| `Arcp.Core`       | Wire-format reference. Every other project references it. Envelope, messages, error taxonomy, IDs, transports, event log. |
| `Arcp.Client`     | `ArcpClient` and `JobHandle` — the side that submits jobs. |
| `Arcp.Runtime`    | `ArcpServer`, `JobManager`, `LeaseManager`, `SessionState` — the side that runs them. |
| `Arcp.AspNetCore` | Mounts a runtime on Kestrel via `IEndpointRouteBuilder.MapArcp("/arcp")`. |
| `Arcp.Otel`       | Wraps `ITransport` with `ActivitySource`-based OTel instrumentation. |
| `Arcp.Hosting`    | Registers `ArcpServer` in DI via `AddArcpRuntime` for non-ASP.NET workers. |
| `Arcp.Cli`        | `arcp serve` / `arcp submit` / `arcp version` executable. |
| `Arcp`            | Umbrella meta-package — `dotnet add package Arcp` pulls Core + Client + Runtime. |

`Arcp.Core` has no third-party dependencies. `Arcp.Client` and
`Arcp.Runtime` reference only `Arcp.Core`. The middleware projects
(`AspNetCore`, `Otel`, `Hosting`) depend on their respective
framework libraries. No circular dependencies.

## Wire format (spec §5)

Every message is a JSON object envelope:

```json
{
  "arcp": "1.1",
  "id":   "msg_01J...",
  "type": "job.submit",
  "session_id": "sess_01J...",
  "trace_id":   "4bf92f3557...",
  "job_id":     null,
  "event_seq":  null,
  "payload": { "...": "..." }
}
```

Unknown top-level fields are preserved verbatim in
`Envelope.Extensions` (`Dictionary<string, JsonElement>`), so
vendor-extension hints round-trip without loss (spec §5).

## Versioning

The SDK follows SemVer strictly. The `arcp` wire version field
defaults to `"1.1"` on `Envelope.Arcp` (also exposed as `Arcp.ArcpInfo.ProtocolVersion`). Adding a
public member is a minor bump; changing a signature is a major bump.
One minor deprecation cycle (`[Obsolete]`) before removal. See the
[style guide](./style-guide.md#14-versioning--compatibility).

## Agent versioning (spec §7.5)

Agents are referenced as `name` or `name@version`. The grammar:

```
agent-ref ::= name ( "@" version )?
name      ::= [a-z0-9][a-z0-9._-]*
version   ::= [a-zA-Z0-9.+_-]+
```

Register multiple versions on the same name:

```csharp
server.RegisterAgentVersion("code-refactor", "1.0.0", new CodeRefactorV1());
server.RegisterAgentVersion("code-refactor", "2.0.0", new CodeRefactorV2());
server.SetDefaultAgentVersion("code-refactor", "2.0.0");
```

The runtime advertises all registered versions in
`session.welcome.payload.capabilities.agents` (spec §6.2):

```json
{
  "name": "code-refactor",
  "versions": ["1.0.0", "2.0.0"],
  "default": "2.0.0"
}
```

`AgentRef` is a `readonly record struct` implementing `IParsable<AgentRef>`:

```csharp
var r = AgentRef.Parse("code-refactor@2.0.0");
// r.Name == "code-refactor", r.Version == "2.0.0"
```

## Session state machine

```
pre-handshake
  ↓  send session.hello
awaiting-welcome
  ↓  receive session.welcome
accepted     ←→  normal message exchange
  ↓  send/receive session.bye  OR  transport closes
closed
```

If the runtime cannot authenticate, it emits `session.error` and
closes the transport instead of `session.welcome`.

## v1.1 feature negotiation

Features are negotiated in the hello/welcome exchange. The effective
set is `intersect(hello.features, welcome.features)`. The C# SDK
advertises all v1.1 features by default (`FeatureSet.AllFeatures`).
Neither peer may use a feature outside the negotiated set.

| Feature              | Spec   | What it enables |
| -------------------- | ------ | --------------- |
| `heartbeat`          | §6.4   | `session.ping` / `session.pong` liveness. |
| `ack`                | §6.5   | `session.ack` flow control. |
| `list_jobs`          | §6.6   | `session.list_jobs` / `session.jobs`. |
| `subscribe`          | §7.6   | Cross-session job observation. |
| `agent_versions`     | §7.5   | `name@version` pinning. |
| `progress`               | §8.2.1 | `progress` job-event kind. |
| `result_chunk`           | §8.4   | Streamed multi-chunk results. |
| `lease_expires_at`       | §9.5   | Time-bounded lease constraints. |
| `cost.budget`            | §9.6   | Per-currency cost ceilings. |
| `model.use`              | §9.7   | `model.use` lease constraint. |
| `provisioned_credentials`| §9.8   | Runtime-issued short-lived credentials. |
