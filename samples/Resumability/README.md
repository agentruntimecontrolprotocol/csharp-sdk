# Resumability

Five-step research job (plan → gather → synthesize → critique →
finalize) that checkpoints after every step. Crash mid-flight,
resume on next invocation, no work lost.

## Before ARCP

Long jobs survive crashes only if the team built their own
checkpoint store, retry contract, and dedupe layer. Most don't.

## With ARCP

```csharp
// every step ends with two envelopes
await EmitProgressAsync(client, jobId, step: "synthesize");
await EmitCheckpointAsync(client, jobId, step: "synthesize");

// resume picks up at the step *after* the last checkpoint
string? last = await IssueResumeAsync(client, jid, afterMsgId, checkpointId);
int nextIdx = Array.IndexOf(steps, label) + 1;
```

Per-step `idempotency_key` keeps execution single across retries.

## Try it

```bash
# crash after `synthesize`. Prints the resume token.
CRASH_AFTER_STEP=synthesize dotnet run --project samples/Resumability

# resume — runtime replays up to the last checkpoint, we run from the next step.
RESUME_JOB_ID=...  RESUME_AFTER_MSG_ID=...  RESUME_CHECKPOINT_ID=... \
  dotnet run --project samples/Resumability
```

## ARCP primitives

- Resumability — RFC §19, `after_message_id` + `checkpoint_id`.
- Job lifecycle + checkpoints — §10.
- `idempotency_key` semantics — §6.4.
- `DATA_LOSS` on retention expiry — §19, §18.2.

## File tour

- `Program.cs` — start-fresh vs resume; `Environment.Exit(137)` on the
  crash step to demonstrate process death.
- `Steps.cs` — step bodies (stubbed).
- `Stubs.cs` — elided client helpers.

## Variations

- Plug a checkpointer that doubles to a SQLite store so checkpoints
  survive ARCP retention expiry too.
- Branch on critique severity: low → finalize; high → loop back to
  synthesize with the critique appended.
- Emit `kind: thought` between steps for [ReasoningStreams](../ReasoningStreams)
  to consume.
