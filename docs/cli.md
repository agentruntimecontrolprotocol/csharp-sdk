# CLI

`Arcp.Cli` ships an `arcp` executable. It is a thin operational tool
for running runtimes, submitting jobs, and inspecting the SDK version.

## Install

```sh
dotnet tool install -g Arcp.Cli
```

Or add the package to a project that depends on it:

```sh
dotnet add package Arcp.Cli
```

## `arcp serve`

Start a runtime that hosts a single named echo agent over WebSocket or
stdio. Most production deployments embed `ArcpServer` programmatically;
`serve` is for ad-hoc testing and reproductions.

```sh
arcp serve \
  --host 127.0.0.1 \
  --port 7777 \
  --token tok \
  --principal me@example.com
```

Flags:

| Flag                        | Default     | Notes |
| --------------------------- | ----------- | ----- |
| `--transport <ws\|stdio>`   | `ws`        | `stdio` makes this a subprocess runtime. |
| `--host <host>`             | `127.0.0.1` | Bind address for WebSocket. |
| `--port <port>`             | `7777`      | Bind port for WebSocket. |
| `--path <path>`             | `/arcp`     | URL path for the WebSocket upgrade. |
| `--token <token>`           | —           | Required. Static bearer accepted by the verifier. |
| `--principal <id>`          | —           | Principal returned when the token verifies. |

## `arcp submit`

Submit one job and print the terminal result. Useful in shell scripts
and CI.

```sh
arcp submit \
  --url ws://127.0.0.1:7777/arcp \
  --token tok \
  --agent my-agent \
  --input '{"hi":1}' \
  --idempotency-key run-2026-W19
```

Flags:

| Flag                      | Notes |
| ------------------------- | ----- |
| `--url <ws-url>`          | Runtime URL. |
| `--token <token>`         | Bearer token. |
| `--agent <name[@ver]>`    | Registered agent name, optionally pinned to a version. |
| `--input <json>`          | Inline JSON payload. |
| `--input-file <path>`     | Read payload from a file (mutually exclusive with `--input`). |
| `--idempotency-key <key>` | Optional deduplication key (spec §7.2). |
| `--max-runtime-sec <n>`   | Hard wall-clock timeout for the job. |
| `--lease <json>`          | Lease object as JSON (spec §9). |
| `--trace-id <hex>`        | 32-hex W3C trace ID to propagate. |

Stdout receives the final `job.result` payload as JSON. Events are
streamed to stderr in human-readable form (`[seq] kind message`).

## `arcp version`

Print the SDK and wire-format versions:

```sh
arcp version
# Arcp.Cli 1.0.0 (wire 1.1)
```

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0`  | Job completed with `final_status: "success"`. |
| `1`  | Runtime/server error (auth failure, bind failure, unknown agent). |
| `2`  | Job terminated with `error`, `cancelled`, or `timed_out`. |
| `64` | Bad CLI arguments. |

## stdio mode

`--transport stdio` makes `arcp serve` read envelopes from stdin and
write them to stdout, turning the process into a child-agent runtime.
The parent is the ARCP client. Pipe agent logs to stderr or silence
them — any non-envelope byte on stdout corrupts the channel.

```csharp
// Parent process in C#:
using var proc = Process.Start(new ProcessStartInfo
{
    FileName  = "arcp",
    Arguments = "serve --transport stdio --token tok --principal me",
    RedirectStandardInput  = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
});
var transport = new StdioTransport(
    proc!.StandardOutput.BaseStream,
    proc.StandardInput.BaseStream);
```

See [Transports — stdio](./transports.md#stdio) for the full pattern.
