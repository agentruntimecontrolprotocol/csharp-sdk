---
title: Agent versioning
sdk: csharp
spec_sections: ["§7.5"]
order: 13
kind: guide
---

Agents are referenced as `name` or `name@version` (spec §7.5). The grammar:

```
name    ::= [a-z0-9][a-z0-9._-]*
version ::= [a-zA-Z0-9.+_-]+
```

## Register multiple versions

```csharp
server.RegisterAgentVersion("code-refactor", "1.0.0", new CodeRefactorV1());
server.RegisterAgentVersion("code-refactor", "2.0.0", new CodeRefactorV2());
server.SetDefaultAgentVersion("code-refactor", "2.0.0");
```

The runtime advertises this on `session.welcome.payload.capabilities.agents`:

```json
{ "name": "code-refactor", "versions": ["1.0.0", "2.0.0"], "default": "2.0.0" }
```

## Resolution rules

| Submitted             | Resolution |
| --------------------- | ---------- |
| `code-refactor`       | Defaults to the advertised `default` version. |
| `code-refactor@2.0.0` | Exact match required. |
| `code-refactor@9.9.9` | `AGENT_VERSION_NOT_AVAILABLE`. |

The resolved version is echoed on `job.accepted.payload.agent` as `name@version` and is fixed for the life of the job — the runtime MUST NOT migrate a running job to a different version.

## AgentRef

`AgentRef` is a `readonly record struct` implementing `IParsable<AgentRef>`:

```csharp
var r = AgentRef.Parse("code-refactor@2.0.0");
r.Name;    // "code-refactor"
r.Version; // "2.0.0"
r.ToString(); // "code-refactor@2.0.0"
```

See [`samples/AgentVersions/`](../samples/AgentVersions/) for a runnable demonstration.
