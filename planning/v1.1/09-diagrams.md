# 09 — Graphviz Diagrams

Plan-only. No `.dot` source is committed here; sources land under
`docs/diagrams/` in the implementation pass.

## 0. Shared style

Style derives from `/Users/nficano/Desktop/files/README.md` (the
paired-template guide used across SDKs). That guide is geared at
**architecture** diagrams: two paired files (`*-light.dot` /
`*-dark.dot`), two anchor types (one ENTRY blue `#3B82F6`, one HUB
amber `#F59E0B`), rounded boxes, `compound=true`, single-line centered
node labels, dashed pink (`#F472B6`) `constraint=false` edges for
async / feedback. We honour that for diagram **(a)** which is the only
true architecture graph. For the FSMs **(b, c)** and sequence-style
graphs **(d, e, f)** we extend with conservative additions that the
guide does not cover:

- FSMs (b, c): `rankdir=TB`, `node [shape=ellipse]` for states,
  terminal states in the HUB amber, a single ENTRY blue start node.
  Other states stay default (white / slate-700).
- Sequence-style graphs (d, e, f): `rankdir=TB`, lifelines as plain
  rounded boxes anchored at `rank=min` with an invisible top edge,
  ordered message arrows numbered in their label (`1: …`, `2: …`). No
  HUB anchor — sequence diagrams are linear, not hub-and-spoke.
- All diagrams: `bgcolor="transparent"`, `fontname="Helvetica"`,
  graph `fontsize=11`, node `fontsize=10`, edge `fontsize=9`,
  `nodesep=0.45`, `ranksep=0.6`, `splines=ortho` for (a, b, c) and
  `splines=spline` for sequence diagrams (orthogonal routing collapses
  parallel message arrows into unreadable overlaps).
- Each diagram ships as a `*-light.dot` / `*-dark.dot` pair; embeds use
  the `<picture>` snippet from the guide's "Render and embed" section.
  Dark variants flip only the palette tokens listed in the guide's
  Palette table — node and edge structure is identical.

If a future spec-citation needs more than one accent colour, fall back
to outline-only emphasis (`penwidth=1.4`, default fill) rather than
introducing a third palette colour — the guide bans that.

Cited: styling guide §"Design rules" + §"Palette".

## 1. Diagram inventory

Six diagrams, all under `docs/diagrams/`. Each row lists the source
pair, the render target, and the primary citation for the labels.

| # | Files (light/dark) | Renders to | Label source |
| - | ------------------ | ---------- | ------------ |
| a | `arcp-projects-light.dot` / `arcp-projects-dark.dot` | `arcp-projects-light.svg` / `arcp-projects-dark.svg` | `planning/v1.1/04-architecture.md` (solution layout) |
| b | `session-fsm-light.dot` / `session-fsm-dark.dot` | `session-fsm-{light,dark}.svg` | spec §6 (handshake), §6.4 (heartbeat), §6.5 (ack) |
| c | `job-fsm-light.dot` / `job-fsm-dark.dot` | `job-fsm-{light,dark}.svg` | spec §7.3 (lifecycle), §7.6 (subscribe), §9.5 (lease), §9.6 (budget) |
| d | `capability-negotiation-light.dot` / `…-dark.dot` | `capability-negotiation-{light,dark}.svg` | spec §6.2 |
| e | `heartbeat-ack-light.dot` / `…-dark.dot` | `heartbeat-ack-{light,dark}.svg` | spec §6.4 + §6.5 |
| f | `result-chunk-progress-light.dot` / `…-dark.dot` | `result-chunk-progress-{light,dark}.svg` | spec §8.2.1 + §8.4 |

Render command for any one diagram:

```bash
dot -Tsvg docs/diagrams/<name>-light.dot -o docs/diagrams/<name>-light.svg
dot -Tsvg docs/diagrams/<name>-dark.dot  -o docs/diagrams/<name>-dark.svg
```

Embed with the `<picture>` snippet from the styling guide's "Render
and embed" section. The docs site (Phase 8) picks SVGs up directly
from `docs/diagrams/` — no extra copy step.

---

### (a) Project dependency graph

- **Files:** `docs/diagrams/arcp-projects-{light,dark}.dot`.
- **Direction:** `rankdir=LR`. Splines `ortho`. Library projects sit
  in an outer cluster `cluster_src` (`src/Arcp.*`), samples in
  `cluster_samples`, tests in `cluster_tests`.
- **Nodes:** `Arcp` (umbrella, marked ENTRY blue — this is the
  package consumers add), `Arcp.Core` (HUB amber — every other
  project references it), `Arcp.Client`, `Arcp.Runtime`,
  `Arcp.AspNetCore`, `Arcp.Otel`, plus `samples/Hello/`,
  `samples/ResultChunk/`, `samples/Budget/`, and a single test node
  `tests/Arcp.Tests` (collapse the per-project test split — one
  rectangle keeps the graph legible).
- **Edges:** `ProjectReference` direction only (consumer → consumed).
  `Arcp` → {`Arcp.Client`, `Arcp.Runtime`, `Arcp.AspNetCore`,
  `Arcp.Otel`}; `Arcp.Client` → `Arcp.Core`; `Arcp.Runtime` →
  `Arcp.Core`; `Arcp.AspNetCore` → `Arcp.Runtime`; `Arcp.Otel` →
  `Arcp.Core`; samples → relevant `Arcp.*`; tests → all `src/Arcp.*`.
  Primary spine for the `Arcp → *` fan-out; secondary tier for the
  test edges so they recede.
- **Rationale:** The two anchors fall out of the design — `Arcp` is
  the one package end users add (`dotnet add package Arcp`), and
  `Arcp.Core` is the message taxonomy every other project hangs off
  of (records, `JsonSerializerContext`, error codes from §12). The
  graph doubles as a visual lint: any future arrow from `Arcp.Core` →
  anything is a layering bug.
- **Source of truth:** `planning/v1.1/04-architecture.md` "Solution /
  project layout" section.

### (b) Session FSM

- **Files:** `docs/diagrams/session-fsm-{light,dark}.dot`.
- **Direction:** `rankdir=TB`. Splines `ortho`.
- **States (nodes, ellipse):** `Connecting` (ENTRY blue, the start),
  `Hello`, `Welcome`, `Active`, `Resuming`, `Closing`, `Closed` (HUB
  amber, terminal).
- **Transitions (labels are spec wire names):**
    - `Connecting` → `Hello` on transport open + send
      `session.hello`.
    - `Hello` → `Welcome` on receive `session.welcome` (carries
      `heartbeat_interval_sec`, §6.4).
    - `Welcome` → `Active` on first envelope exchanged after handshake
      (no wire name — internal).
    - `Active` → `Active` self-loop labelled `session.ping` /
      `session.pong` (§6.4) and a second self-loop labelled
      `session.ack { last_processed_seq }` (§6.5).
    - `Active` → `Closing` on either side sending `session.bye`.
    - `Active` → `Resuming` on transport drop with resume window
      still open.
    - `Resuming` → `Active` on successful re-`session.hello` with
      `resume_token`.
    - `Resuming` → `Closed` on `RESUME_WINDOW_EXPIRED` (§12).
    - `Active` → `Closed` on heartbeat loss labelled `HEARTBEAT_LOST`
      (two missed intervals, §6.4 + §12).
    - `Closing` → `Closed` on transport close.
- **Cites:** spec §6 (overall handshake), §6.4 (ping/pong +
  `HEARTBEAT_LOST`), §6.5 (`ack`).

### (c) Job FSM (v1.1)

- **Files:** `docs/diagrams/job-fsm-{light,dark}.dot`.
- **Direction:** `rankdir=TB`. Splines `ortho`.
- **States:** `Pending` (ENTRY blue, entered on `job.submit`),
  `Running`, `Success` (HUB amber, terminal), `Error` (HUB amber,
  terminal), `Cancelled` (terminal, default fill, `penwidth=1.4`),
  `TimedOut` (terminal, default fill, `penwidth=1.4`). The styling
  guide allows only one HUB; we pick `Success` and `Error` as a
  visually-paired terminal **but** drop one of them to default + thick
  border if the renderer balks — the constraint is one-amber-anchor
  per diagram. Decision: keep amber only on `Success`; `Error`,
  `Cancelled`, `TimedOut` get the thick-border default treatment so
  the palette rule is not violated.
- **Transitions:**
    - `Pending` → `Running` on `job.accepted` (§7.3).
    - `Running` → `Success` on `job.result { result_id?, result_size?, summary? }` (§8.4).
    - `Running` → `Error` on `job.error { final_status: "error", code }` where `code` is any of `INVALID_REQUEST | PERMISSION_DENIED | INTERNAL_ERROR | LEASE_EXPIRED | BUDGET_EXHAUSTED | …` (§9.5, §9.6, §12). The two v1.1 codes ride this same edge — they don't add states; annotate the edge label with `code ∈ {…, LEASE_EXPIRED, BUDGET_EXHAUSTED}` so readers see v1.1 surfaces here.
    - `Running` → `Cancelled` on `job.cancel` → `CANCELLED` terminal.
    - `Running` → `TimedOut` on `lease.timeout` → `TIMEOUT` terminal (distinct from `LEASE_EXPIRED`, which is an §9.5 error edge).
    - **Sidecar (subscribe) arrow:** a dashed pink `constraint=false`
      self-loop on `Running` labelled `job.subscribe → job.subscribed`
      (§7.6). The `constraint=false` is exactly the guide's feedback
      pattern — keeps the sidecar out of the layout solver so the main
      spine still reads top-to-bottom.
    - **Unsubscribe no-op:** a second dashed pink self-loop on
      `Running` labelled `job.unsubscribe (no state change)` (§7.6).
- **Cites:** spec §7.3 (terminal contract), §7.6 (subscribe /
  unsubscribe), §9.5 (`LEASE_EXPIRED`), §9.6 (`BUDGET_EXHAUSTED`).

### (d) Capability negotiation sequence

- **Files:** `docs/diagrams/capability-negotiation-{light,dark}.dot`.
- **Direction:** `rankdir=TB`, `splines=spline`.
- **Lifelines (rounded boxes, `rank=min`):** `Client` (ENTRY blue),
  `Runtime`. No HUB — sequence is linear.
- **Messages (numbered):**
    1. `Client → Runtime`: `session.hello { capabilities.features: A }`
       — where `A` is the client's advertised set (e.g.
       `{heartbeat, ack, subscribe, result_chunk, progress, agent_versions, lease_expires_at, cost.budget}`).
    2. `Runtime → Client`: `session.welcome { capabilities.features: B, capabilities.agents: [...] }`.
    3. Annotation node (default rounded box, dashed grey border):
       "Effective set = A ∩ B. Neither peer MUST use a feature outside
       the intersection (§6.2)."
- **Cites:** spec §6.2.

### (e) Heartbeat + ack flow

- **Files:** `docs/diagrams/heartbeat-ack-{light,dark}.dot`.
- **Direction:** `rankdir=TB`, `splines=spline`.
- **Lifelines:** `Client` (ENTRY blue), `Runtime`.
- **Messages (numbered, top to bottom):**
    1. Idle gap annotation: "no traffic for `heartbeat_interval_sec`
       (advertised in `session.welcome`)".
    2. `Runtime → Client`: `session.ping { nonce, sent_at }` (§6.4).
    3. `Client → Runtime`: `session.pong { ping_nonce, received_at }`
       (§6.4). Ping and pong are excluded from `event_seq` (annotate).
    4. In parallel (dashed pink, `constraint=false`): `Client →
       Runtime`: `session.ack { last_processed_seq }` (§6.5).
    5. Conditional back-pressure event (dashed pink): `Runtime →
       Client`: `status { phase: "back_pressure" }` when
       `highWatermark − lastAck` exceeds the configured threshold
       (§6.5). Label notes this is emitted out of the regular ack
       cadence, not on every ack.
- **Cites:** spec §6.4 + §6.5.

### (f) `result_chunk` + `progress` event sequence

- **Files:** `docs/diagrams/result-chunk-progress-{light,dark}.dot`.
- **Direction:** `rankdir=TB`, `splines=spline`.
- **Lifelines:** `Client` (ENTRY blue), `Runtime`. A right-margin
  annotation cluster (`cluster_client_code`, inner-fill `#F8FAFC` /
  `#1E293B`) labels the client-side surface so readers see the C#
  shape next to the wire.
- **Messages (numbered):**
    1. `Client → Runtime`: `job.submit { agent, input }` (context
       only).
    2. `Runtime → Client`: `job.accepted { job_id, agent, lease }`
       (§7.3).
    3. Interleaved emission loop (each arrow numbered `3a`, `3b`, …):
       - `job.event { kind: "progress", body: { current, total?, units?, message? } }` (§8.2.1).
       - `job.event { kind: "result_chunk", body: { result_id, chunk_seq: 0, data, encoding, more: true } }` (§8.4).
       - `job.event { kind: "progress", … }`.
       - `job.event { kind: "result_chunk", body: { result_id, chunk_seq: 1, more: true } }`.
       - … through `chunk_seq: N` with `more: false`.
    4. Terminal: `Runtime → Client`: `job.result { result_id, result_size, summary? }` (§8.4).
- **Client-side annotation (right cluster):** two boxes — one labelled
  `await foreach (var chunk in handle.Chunks(ct)) { … }`, one labelled
  `IProgress<ProgressBody> onProgress`. A dashed pink
  `constraint=false` arrow from each `result_chunk` arrow to the
  `Chunks(ct)` box, and from each `progress` arrow to the `IProgress`
  box, makes the wire → C# mapping legible without distorting the
  vertical message order.
- **Cites:** spec §8.2.1 + §8.4. Client-surface shape mirrors
  `planning/v1.1/04-architecture.md` (`JobHandle.Chunks(CancellationToken)`)
  and the example index in `planning/v1.1/06-examples.md`
  (`samples/ResultChunk/`).

---

## 2. Build integration

A single one-shot renderer renders every `.dot` to its sibling `.svg`.
Two equivalent forms — ship whichever the docs CI already prefers.

### Option A: `docs/diagrams/Makefile`

```make
DOTS := $(wildcard *.dot)
SVGS := $(DOTS:.dot=.svg)

.PHONY: all clean
all: $(SVGS)

%.svg: %.dot
	dot -Tsvg $< -o $@

clean:
	rm -f $(SVGS)
```

Invoke from repo root:

```bash
make -C docs/diagrams
```

### Option B: `docs/diagrams/build.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
for f in *.dot; do
  dot -Tsvg "$f" -o "${f%.dot}.svg"
done
```

The docs site (see Phase 8) reads `docs/` recursively; SVGs in
`docs/diagrams/` are picked up by the same static-site pipeline that
ingests the Markdown. No separate publishing step.

## 3. Toolchain prerequisite

`graphviz` (provides `dot`) must be on `PATH`. Install one-liner per
OS:

- macOS (Homebrew): `brew install graphviz`
- Debian / Ubuntu: `sudo apt-get install -y graphviz`
- Windows (winget): `winget install Graphviz.Graphviz`

CI runners that build docs need the same — the docs job adds one of
the above before invoking the Makefile.

## 4. What this phase does not cover

- No `.dot` source. Sources land alongside the implementation PR for
  each feature so the diagram and the code review the same delta.
- No diagram for sub-leasing (§9.4). The lease / budget rules sit
  inside the job FSM as edge labels; a separate sub-lease diagram is a
  candidate for v1.2 once federation lands and the parent / child
  relationship gains structural complexity.
- No diagram for the `list_jobs` request/response (§6.6). It is a
  single request/response pair with no FSM impact — prose in Phase 8
  covers it without a picture.
