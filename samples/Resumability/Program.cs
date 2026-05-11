// Durable research job with real crash and resume.
//
//   # First call: crash after `synthesize`. Prints the resume token.
//   CRASH_AFTER_STEP=synthesize \
//     dotnet run --project samples/Resumability
//
//   # Second call: pick up from the printed checkpoint.
//   RESUME_JOB_ID=...  RESUME_AFTER_MSG_ID=...  RESUME_CHECKPOINT_ID=... \
//     dotnet run --project samples/Resumability
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Control;
using ARCP.Messages.Execution;
using ARCP.Messages.Telemetry;
using ARCP.Samples.Resumability;
using static ARCP.Samples.Resumability.ClientStubs;
using Env = ARCP.Envelope.Envelope;

string[] steps = ["plan", "gather", "synthesize", "critique", "finalize"];

static string StepKey(JobId jobId, string step, string salt)
{
    // Deterministic per-step idempotency key (RFC §6.4). Re-issuing the
    // same step with the same input returns the prior outcome instead of
    // re-running the LLM.
    using SHA256 h = SHA256.Create();
    foreach (string piece in (string[])[jobId.Value, step, salt])
    {
        h.TransformBlock(Encoding.UTF8.GetBytes(piece), 0, Encoding.UTF8.GetByteCount(piece), null, 0);
        h.TransformBlock([0], 0, 1, null, 0);
    }
    h.TransformFinalBlock([], 0, 0);
    string hex = Convert.ToHexString(h.Hash!).ToLowerInvariant()[..16];
    return $"research:{jobId}:{step}:{hex}";
}

async Task EmitProgressAsync(ARCPClient client, JobId jobId, string step)
{
    double pct = 100.0 * (Array.IndexOf(steps, step) + 1) / steps.Length;
    await Send(client, Envelope(
        client,
        "job.progress",
        new JobProgress(Percent: pct, Message: step),
        jobId: jobId));
}

async Task<string> EmitCheckpointAsync(ARCPClient client, JobId jobId, string step)
{
    string chk = $"chk_{step}_{jobId.Value[^6..]}";
    await Send(client, Envelope(
        client,
        "job.checkpoint",
        new JobCheckpoint(CheckpointId: chk),
        jobId: jobId));
    return chk;
}

async Task<object> ExecuteStepsAsync(
    ARCPClient client,
    JobId jobId,
    object request,
    string startingAt,
    string? crashAfter)
{
    object output = request;
    foreach (string step in steps)
    {
        if (Array.IndexOf(steps, step) < Array.IndexOf(steps, startingAt)) continue;
        string key = StepKey(jobId, step, output.ToString() ?? string.Empty);
        await EmitProgressAsync(client, jobId, step);
        output = await Steps.RunStepAsync(client, jobId, step, new Dictionary<string, object>
        {
            ["prior"] = output,
            ["idempotency_key"] = key,
        });
        await EmitCheckpointAsync(client, jobId, step);
        if (crashAfter == step)
        {
            // The whole point of durable jobs: process death is fine.
            // Runtime kept every envelope; resume picks it up.
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[crash after {0}; resume with RESUME_JOB_ID={1} RESUME_CHECKPOINT_ID=chk_{0}_{2} RESUME_AFTER_MSG_ID=<last id from your event log>]",
                step, jobId, jobId.Value[^6..]));
            Environment.Exit(137);
        }
    }
    return output;
}

async Task<string?> IssueResumeAsync(
    ARCPClient client,
    JobId jobId,
    MessageId afterMessageId,
    string? checkpointId)
{
    // Replay envelopes; return the last checkpoint label, or null if the
    // job already terminated during replay.
    await Send(client, Envelope(
        client,
        "resume",
        new Resume(AfterMessageId: afterMessageId, CheckpointId: checkpointId, IncludeOpenStreams: true),
        jobId: jobId));

    string? last = null;
    await foreach (Env env in Events(client))
    {
        if (!Equals(env.JobId, jobId)) continue;
        if (env.Type == "tool.error" && ((ToolError)env.Payload).Code == ErrorCode.DataLoss)
        {
            throw new ARCPException(ErrorCode.DataLoss, "retention expired");
        }
        if (env.Type == "job.checkpoint")
        {
            last = ((JobCheckpoint)env.Payload).CheckpointId;
        }
        else if (env.Type is "job.completed" or "job.failed" or "job.cancelled")
        {
            return null;
        }
        else if (env.Type == "event.emit"
                 && ((EventEmit)env.Payload).Name == "subscription.backfill_complete")
        {
            return last; // replay window closed; we're now live
        }
    }
    return last;
}

ARCPClient client = null!; // transport, identity, auth elided
await Open(client);

string? rjId = Environment.GetEnvironmentVariable("RESUME_JOB_ID");
string? rjAfter = Environment.GetEnvironmentVariable("RESUME_AFTER_MSG_ID");
if (rjId is not null && rjAfter is not null)
{
    JobId jid = new(rjId);
    string? last = await IssueResumeAsync(
        client, jid, new MessageId(rjAfter),
        Environment.GetEnvironmentVariable("RESUME_CHECKPOINT_ID"));
    if (last is null)
    {
        Console.WriteLine("already terminal during replay");
    }
    else
    {
        // Find next step from "chk_<step>_<jobtail>".
        string label = last.StartsWith("chk_", StringComparison.Ordinal)
            ? last.Split('_')[1]
            : last;
        int nextIdx = Array.IndexOf(steps, label) + 1;
        if (nextIdx >= steps.Length)
        {
            Console.WriteLine("nothing to resume");
        }
        else
        {
            Console.WriteLine($"[resuming at {steps[nextIdx]}]");
            object final = await ExecuteStepsAsync(
                client, jid, request: "<replayed>",
                startingAt: steps[nextIdx], crashAfter: null);
            await Send(client, Envelope(
                client,
                "job.completed",
                new JobCompleted(Result: JsonSerializer.SerializeToElement(final)),
                jobId: jid));
        }
    }
}
else
{
    JobId jobId = new($"job_{Guid.NewGuid():N}"[..16]);
    string request = "Survey CRDT-based collaborative editing in 2026.";
    await Send(client, Envelope(
        client,
        "workflow.start",
        new WorkflowStart(
            Workflow: "research.v1",
            Inputs: new Dictionary<string, JsonElement>
            {
                ["request"] = JsonSerializer.SerializeToElement(request),
            }),
        jobId: jobId));
    object final = await ExecuteStepsAsync(
        client, jobId, request,
        startingAt: steps[0],
        crashAfter: Environment.GetEnvironmentVariable("CRASH_AFTER_STEP"));
    await Send(client, Envelope(
        client,
        "job.completed",
        new JobCompleted(Result: JsonSerializer.SerializeToElement(final)),
        jobId: jobId));
    Console.WriteLine($"job_id={jobId}\n{final}");
}

await client.CloseAsync();
