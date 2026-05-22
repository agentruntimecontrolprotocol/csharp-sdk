# Arcp.Cli

`Arcp.Cli` installs the `arcp` global tool — a command-line interface for
serving agents and submitting jobs without writing any application code.

```sh
dotnet tool install --global Arcp.Cli
```

## Commands

### `arcp serve`

Start an ARCP server that loads agents from a plugin assembly:

```sh
arcp serve --assembly MyAgents.dll \
           --address http://127.0.0.1:7777/arcp \
           --token   tok-dev
```

The server registers every type in the assembly that implements `IAgent` and
accepts incoming connections on the given address.

### `arcp submit`

Submit a job to a running server and stream the results to stdout:

```sh
arcp submit --address http://127.0.0.1:7777/arcp \
            --token   tok-dev \
            --agent   echo \
            --input   '{"message":"hello"}'
```

Log events are printed as they arrive. The exit code reflects the job
terminal state (see Exit codes below).

### `arcp version`

Print the tool version and the ARCP wire version it speaks:

```sh
arcp version
# arcp 1.1.4 (wire arcp/1.1)
```

## Stdio mode

`arcp submit` can proxy stdin/stdout as a stdio transport when you pass
`--stdio` instead of `--address`. This lets shell scripts call agents as
subprocesses:

```sh
echo '{"message":"hello"}' | arcp submit --stdio --agent echo
```

## Exit codes

| Code | Meaning                              |
| ---- | ------------------------------------ |
| `0`  | Job completed successfully.          |
| `1`  | Job failed (`INTERNAL_ERROR`, etc.). |
| `2`  | Usage error (bad flags, parse error).|
| `3`  | Connection / authentication failure. |

## Configuration file

Place an `arcp.json` in the working directory to avoid repeating flags:

```json
{
  "address": "http://127.0.0.1:7777/arcp",
  "token":   "tok-dev"
}
```

Command-line flags take precedence over `arcp.json`.

## Related

- [CLI reference](../cli.md) — full flag reference and examples.
- [Transports — stdio](../transports.md#stdio) — stdio transport details.
- [Getting started](../getting-started.md) — first-run walkthrough.
