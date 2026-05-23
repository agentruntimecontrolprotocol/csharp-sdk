# CLI

`Arcp.Cli` ships an `arcp` executable. It is a thin operational tool
for running a demo runtime, submitting one-off jobs, and printing the
SDK version.

## Install

```sh
dotnet tool install -g Arcp.Cli
```

Or add the package to a project that depends on it:

```sh
dotnet add package Arcp.Cli
```

## `arcp serve`

Start a runtime that hosts a single `echo` agent over WebSocket. Most
production deployments embed `ArcpServer` programmatically; `serve` is
for ad-hoc testing and reproductions.

```sh
arcp serve \
  --host 127.0.0.1 \
  --port 7777 \
  --token tok
```

Flags:

| Flag              | Default     | Notes |
| ----------------- | ----------- | ----- |
| `--host <host>`   | `127.0.0.1` | Bind address for WebSocket. |
| `--port <port>`   | `7777`      | Bind port for WebSocket. |
| `--token <token>` | `tok-demo`  | Static bearer accepted by the verifier. |

The runtime accepts WebSocket upgrades at the path `/arcp` and exposes
`/healthz` for liveness probes.

## `arcp submit`

Submit one job and print the terminal result. Useful in shell scripts
and CI.

```sh
arcp submit \
  --url ws://127.0.0.1:7777/arcp \
  --token tok \
  --agent echo \
  --input '{"hi":1}'
```

Flags:

| Flag              | Default                    | Notes |
| ----------------- | -------------------------- | ----- |
| `--url <ws-url>`  | `ws://127.0.0.1:7777/arcp` | Runtime URL. |
| `--token <token>` | `tok-demo`                 | Bearer token. |
| `--agent <name>`  | `echo`                     | Registered agent name. |
| `--input <json>`  | `{}`                       | Inline JSON payload. |

Stdout receives `connected:` and `accepted:` status lines plus the
final `result: status=...` line.

## `arcp version`

Print the protocol version:

```sh
arcp version
# arcp 1.1
```

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0`  | Command succeeded (`submit` requires `final_status: "success"`). |
| `1`  | Submit terminated with a non-success status. |
| `2`  | Unknown subcommand. |

For richer flags (idempotency keys, stdio transport, lease constraints,
trace IDs, file-based input), drive `ArcpClient` directly from a small
host program — the CLI is intentionally minimal. See
[Recipes](./recipes.md) and the [samples](../samples/) for end-to-end
examples.
