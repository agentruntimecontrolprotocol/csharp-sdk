# Arcp.Cli

`Arcp.Cli` installs the `arcp` global tool — a thin command-line wrapper
that runs a demo `echo` runtime and submits jobs against one. It is
intended for ad-hoc testing and reproductions, not as a generic
agent host.

```sh
dotnet tool install --global Arcp.Cli
```

## Commands

### `arcp serve`

Start an ARCP runtime that registers a single built-in `echo` agent and
listens for WebSocket upgrades on `/arcp`:

```sh
arcp serve --host 127.0.0.1 --port 7777 --token tok-dev
```

| Flag              | Default     |
| ----------------- | ----------- |
| `--host <host>`   | `127.0.0.1` |
| `--port <port>`   | `7777`      |
| `--token <token>` | `tok-demo`  |

`/healthz` is exposed alongside the ARCP endpoint for liveness probes.

### `arcp submit`

Submit one job to a running runtime and print the terminal status:

```sh
arcp submit --url ws://127.0.0.1:7777/arcp \
            --token tok-dev \
            --agent echo \
            --input '{"message":"hello"}'
```

| Flag              | Default                    |
| ----------------- | -------------------------- |
| `--url <ws-url>`  | `ws://127.0.0.1:7777/arcp` |
| `--token <token>` | `tok-demo`                 |
| `--agent <name>`  | `echo`                     |
| `--input <json>`  | `{}`                       |

### `arcp version`

Print the protocol version the CLI speaks:

```sh
arcp version
# arcp 1.1
```

## Exit codes

| Code | Meaning                                        |
| ---- | ---------------------------------------------- |
| `0`  | Command succeeded (`submit` requires success). |
| `1`  | Submit terminated with a non-success status.   |
| `2`  | Unknown subcommand.                            |

## Hosting real agents

The CLI does not load external assemblies, support stdio, or accept a
config file. To host real agents, write a small host program against
`Arcp.Runtime` (and `Arcp.AspNetCore` for WebSocket transport). See
[Getting started](../getting-started.md) and the
[samples](../../samples/).

## Related

- [CLI reference](../cli.md) — top-level CLI documentation.
- [Arcp.Runtime](./Arcp.Runtime.md) — embed `ArcpServer` in your own host.
- [Getting started](../getting-started.md) — first-run walkthrough.
