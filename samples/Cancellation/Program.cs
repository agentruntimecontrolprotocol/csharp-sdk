// Two scenarios over the §10.4 / §10.5 control surface.
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Control;
using ARCP.Messages.Execution;
using static ARCP.Samples.Cancellation.ClientStubs;
using Env = ARCP.Envelope.Envelope;

const int CancelDeadlineMs = 5_000;

string which = args.Length > 0 ? args[0] : "cancel";
if (which == "cancel")
{
    await ScenarioCancelAsync();
}
else if (which == "interrupt")
{
    await ScenarioInterruptAsync();
}
else
{
    throw new InvalidOperationException($"unknown scenario: {which}");
}

static async Task<JobId> StartLongJobAsync(ARCPClient client)
{
    Env accepted = await Request(
        client,
        Envelope(client, "tool.invoke", new ToolInvoke(
            Tool: "demo.long_running",
            Arguments: System.Text.Json.JsonSerializer.SerializeToElement(new { work_seconds = 600 }))),
        timeout: TimeSpan.FromSeconds(10));
    return ((JobAccepted)accepted.Payload).JobId;
}

static async Task<Env> CancelJobAsync(ARCPClient client, JobId jobId, string reason, int deadlineMs)
{
    // Cooperative cancel. Runtime drives target to a clean checkpoint
    // inside `deadline_ms` before terminating; escalates to ABORTED on
    // timeout (RFC §10.4).
    Env reply = await Request(
        client,
        Envelope(client, "cancel", new Cancel(
            Target: CancelTarget.Job,
            TargetId: jobId.Value,
            Reason: reason,
            DeadlineMs: deadlineMs)),
        timeout: TimeSpan.FromMilliseconds(deadlineMs + 5_000));
    if (reply.Type == "cancel.refused")
    {
        CancelRefused refused = (CancelRefused)reply.Payload;
        throw new ARCPException(ErrorCode.FailedPrecondition, $"cancel refused: {refused.Reason}");
    }
    return reply;
}

static Task InterruptJobAsync(ARCPClient client, JobId jobId, string prompt) =>
    // Distinct from cancel: pauses the job (`blocked`), runtime emits
    // `human.input.request`. Job is NOT terminated (RFC §10.5).
    Send(client, Envelope(client, "interrupt", new Interrupt(
        Target: CancelTarget.Job,
        TargetId: jobId.Value,
        Prompt: prompt)));

static async Task<Env> AwaitTerminalAsync(ARCPClient client, JobId jobId)
{
    await foreach (Env env in Events(client))
    {
        if (!Equals(env.JobId, jobId)) continue;
        if (env.Type is "job.completed" or "job.failed" or "job.cancelled") return env;
    }
    throw new InvalidOperationException("event stream closed before terminal");
}

static async Task ScenarioCancelAsync()
{
    ARCPClient client = null!; // transport, identity, auth elided
    await Open(client);
    try
    {
        JobId jobId = await StartLongJobAsync(client);
        await Task.Delay(TimeSpan.FromSeconds(2)); // let the job actually start
        Env ack = await CancelJobAsync(client, jobId, reason: "user_aborted", deadlineMs: CancelDeadlineMs);
        Console.WriteLine($"cancel ack: {ack.Type}");
        Env terminal = await AwaitTerminalAsync(client, jobId);
        Console.WriteLine($"terminal: {terminal.Type}");
    }
    finally
    {
        await client.CloseAsync();
    }
}

static async Task ScenarioInterruptAsync()
{
    ARCPClient client = null!;
    await Open(client);
    try
    {
        JobId jobId = await StartLongJobAsync(client);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await InterruptJobAsync(client, jobId, prompt: "Pause and ask before touching production tables.");
        // Runtime now emits human.input.request; answer via samples/HumanInput.
        await foreach (Env env in Events(client))
        {
            if (env.Type == "human.input.request" && Equals(env.JobId, jobId))
            {
                ARCP.Messages.Human.HumanInputRequest req = (ARCP.Messages.Human.HumanInputRequest)env.Payload;
                Console.WriteLine($"awaiting human: \"{req.Prompt}\"");
                return;
            }
        }
    }
    finally
    {
        await client.CloseAsync();
    }
}
