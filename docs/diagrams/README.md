# Diagrams

Paired light/dark Graphviz diagrams for the ARCP C# SDK. Edit the `.dot` sources; render with `dot -Tsvg`.

## Project graph

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="arcp-projects-dark.svg">
  <img alt="ARCP C# project dependency graph" src="arcp-projects-light.svg">
</picture>

## Session FSM

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="session-fsm-dark.svg">
  <img alt="ARCP session FSM" src="session-fsm-light.svg">
</picture>

## Job FSM

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="job-fsm-dark.svg">
  <img alt="ARCP job FSM" src="job-fsm-light.svg">
</picture>

## Capability negotiation

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="capability-negotiation-dark.svg">
  <img alt="ARCP capability negotiation sequence" src="capability-negotiation-light.svg">
</picture>

## Heartbeat + ack

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="heartbeat-ack-dark.svg">
  <img alt="ARCP heartbeat + ack flow" src="heartbeat-ack-light.svg">
</picture>

## Result chunks + progress

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="result-chunk-progress-dark.svg">
  <img alt="ARCP result_chunk + progress sequence" src="result-chunk-progress-light.svg">
</picture>

## Render

```sh
cd docs/diagrams
for f in *.dot; do dot -Tsvg "$f" -o "${f%.dot}.svg"; done
```

`graphviz` provides `dot`. On macOS: `brew install graphviz`. On Debian/Ubuntu: `apt-get install -y graphviz`.
