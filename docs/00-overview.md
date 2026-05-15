---
title: ARCP v1.1 — overview
sdk: csharp
spec_sections: ["§1", "§2", "§3"]
order: 0
kind: overview
---

ARCP (Agent Runtime Control Protocol) is a transport-agnostic wire protocol for submitting, observing, and controlling long-running AI agent jobs. v1.1 extends v1.0 with explicit liveness signaling, event acknowledgement, job introspection, cross-session subscription, agent versioning, time-bounded leases, budget enforcement, structured progress, and streamed results.

This SDK is the reference C# / .NET 10 implementation. It is split into seven libraries that mirror the TypeScript reference (`@arcp/core`, `@arcp/client`, `@arcp/runtime`, plus host-side middleware). See [`02-architecture.md`](./02-architecture.md) for the project graph.

The single hop you make to be productive: connect a client, register an agent, submit a job. The 20-line quickstart in [`01-quickstart.md`](./01-quickstart.md) is the canonical example.
