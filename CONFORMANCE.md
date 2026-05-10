# ARCP C# SDK — Conformance Matrix

Tracks the implementation status of each RFC-0001-v2 section in this SDK.

Legend: ✅ implemented · 🟡 partial · ⏳ deferred to v0.2 · ❌ not in v0.1.

| §    | Section                              | Status | Notes                                                    |
| ---- | ------------------------------------ | ------ | -------------------------------------------------------- |
| 6.1  | Envelope                             | ⏳     | Phase 1                                                  |
| 6.2  | Message types                        | ⏳     | Phase 1–5                                                |
| 6.3  | Command/result/event flow            | ⏳     | Phase 2–3                                                |
| 6.4  | Delivery semantics + idempotency     | ⏳     | Phase 1                                                  |
| 6.5  | Priority and QoS                     | ⏳     | Phase 1                                                  |
| 7    | Capability negotiation               | ⏳     | Phase 2                                                  |
| 8    | Authentication & identity            | 🟡 ⏳  | Phase 2 (`bearer`/`signed_jwt`/`none` only)              |
| 9    | Sessions                             | 🟡 ⏳  | stateless + stateful in v0.1; durable in v0.2            |
| 10   | Jobs                                 | 🟡 ⏳  | Phase 3 (no `job.schedule`)                              |
| 11   | Streaming                            | 🟡 ⏳  | Phase 3 (base64 only; no sidecar frames)                 |
| 12   | Human-in-the-loop                    | ⏳     | Phase 4                                                  |
| 13   | Subscriptions                        | ⏳     | Phase 5                                                  |
| 14   | Multi-agent                          | ❌     | Deferred (`agent.delegate`/`agent.handoff` parse-only)   |
| 15   | Permissions & leases                 | 🟡 ⏳  | Phase 4 (no §15.6 trust elevation)                       |
| 16   | Artifacts                            | 🟡 ⏳  | Phase 5 (inline base64 only)                             |
| 17   | Observability                        | ⏳     | Phase 1–5                                                |
| 18   | Error model                          | ⏳     | Phase 1                                                  |
| 19   | Resumability                         | 🟡 ⏳  | Phase 5 (msg-id resume only; no checkpoint)              |
| 20   | MCP compatibility                    | n/a    | Documentation-only                                       |
| 21   | Extensions                           | ⏳     | Phase 1                                                  |
| 22   | Reference transports                 | 🟡 ⏳  | Phase 6 (WebSocket + stdio; no HTTP/2, QUIC)             |

This document is updated at the end of each phase. v0.1 release requires every
row to be either ✅ or labeled with a defensible "deferred to v0.2" note.
