// Supervisor + worker pool. Heartbeat loss reroutes via idempotency_key.
using System.Text.Json;
using ARCP.Client;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Execution;
using ARCP.Samples.Heartbeats;
using static ARCP.Samples.Heartbeats.ClientStubs;
using Env = ARCP.Envelope.Envelope;

const int HeartbeatIntervalSeconds = 15;
const int DeadlineSeconds = HeartbeatIntervalSeconds * 2; // RFC §10.3 default N=2

ARCPClient supervisor = null!; // transport, identity (privileged), auth elided
await Open(supervisor);

Roster roster = new();
Dictionary<JobId, WorkTask> jobsToTasks = new();

// In production each worker is its own process; co-hosted here for the demo.
List<Task> workers = [];
foreach (string role in (string[])["indexer", "extractor", "archiver"])
{
    for (int i = 0; i < 2; i++)
    {
        ARCPClient w = null!; // worker session, capabilities advertise role
        await Open(w);
        workers.Add(Task.Run(() => RunWorkerAsync(w)));
        roster.Add(new Worker(
            workerId: $"{role}-{Guid.NewGuid():N}"[..6],
            role: role,
            lastHeartbeat: DateTimeOffset.UtcNow));
    }
}

_ = Task.Run(() => SuperviseAsync(supervisor, roster, jobsToTasks));

for (int n = 0; n < 6; n++)
{
    string role = ((string[])["indexer", "extractor", "archiver"])[n % 3];
    await DispatchAsync(supervisor, new WorkTask(
        TaskId: $"t{n:D3}",
        Role: role,
        Payload: JsonSerializer.SerializeToElement(new { shard = n }),
        IdempotencyKey: new IdempotencyKey($"openclaw:t{n:D3}")), roster, jobsToTasks);
}

await Task.Delay(TimeSpan.FromSeconds(60));
await supervisor.CloseAsync();

// Supervisor side --------------------------------------------------------

static async Task DispatchAsync(
    ARCPClient client,
    WorkTask task,
    Roster roster,
    Dictionary<JobId, WorkTask> jobsToTasks)
{
    List<Worker> candidates = roster.Candidates(task.Role);
    if (candidates.Count == 0)
    {
        throw new InvalidOperationException($"no idle workers for role={task.Role}");
    }
    Worker worker = candidates.MinBy(w => w.LastHeartbeat)!;
    // Same idempotency_key on every re-dispatch (RFC §6.4): a worker that
    // survived the network blip dedupes; it doesn't re-execute.
    Env accepted = await Request(
        client,
        Envelope(
            client,
            "agent.delegate",
            new AgentDelegate(
                Target: worker.WorkerId,
                Task: task.TaskId,
                Context: JsonSerializer.SerializeToElement(new { task_payload = task.Payload })),
            idempotencyKey: task.IdempotencyKey),
        timeout: TimeSpan.FromSeconds(10));
    JobId jobId = ((JobAccepted)accepted.Payload).JobId;
    worker.InFlightJob = jobId;
    jobsToTasks[jobId] = task;
}

static async Task SuperviseAsync(
    ARCPClient client,
    Roster roster,
    Dictionary<JobId, WorkTask> jobsToTasks)
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (Worker w in roster.Workers.Values.ToList())
            {
                if ((now - w.LastHeartbeat).TotalSeconds <= DeadlineSeconds) continue;
                if (w.InFlightJob is { } jid && jobsToTasks.Remove(jid, out WorkTask? task) && task is not null)
                {
                    await DispatchAsync(client, task, roster, jobsToTasks);
                }
                roster.Remove(w);
            }
        }
    });

    await foreach (Env env in Events(client))
    {
        if (env.Type == "job.heartbeat")
        {
            foreach (Worker w in roster.Workers.Values)
            {
                if (w.InFlightJob.Equals(env.JobId))
                {
                    w.LastHeartbeat = DateTimeOffset.UtcNow;
                }
            }
        }
        else if (env.Type is "job.completed" or "job.failed" or "job.cancelled")
        {
            if (env.JobId is { } jid) jobsToTasks.Remove(jid);
            foreach (Worker w in roster.Workers.Values)
            {
                if (w.InFlightJob.Equals(env.JobId)) w.InFlightJob = null;
            }
        }
    }
}

// Worker side ------------------------------------------------------------

static async Task HeartbeatLoopAsync(ARCPClient client, JobId jobId, CancellationToken stop)
{
    long seq = 0;
    while (!stop.IsCancellationRequested)
    {
        await Send(client, Envelope(
            client,
            "job.heartbeat",
            new JobHeartbeat(Sequence: seq++, DeadlineMs: HeartbeatIntervalSeconds * 2000, State: JobState.Running),
            jobId: jobId));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), stop);
        }
        catch (OperationCanceledException) { }
    }
}

static async Task ExecuteAsync(ARCPClient client, Env env)
{
    JobId jobId = new($"job_{Guid.NewGuid():N}"[..14]);
    await Send(client, Envelope(
        client,
        "job.accepted",
        new JobAccepted(jobId, DateTimeOffset.UtcNow),
        jobId: jobId,
        correlationId: env.Id));
    await Send(client, Envelope(
        client,
        "job.started",
        new JobStarted(jobId, DateTimeOffset.UtcNow),
        jobId: jobId));

    using CancellationTokenSource stop = new();
    Task hb = Task.Run(() => HeartbeatLoopAsync(client, jobId, stop.Token));
    try
    {
        AgentDelegate del = (AgentDelegate)env.Payload;
        JsonElement payload = del.Context ?? default;
        JsonElement result = await Work.DoWorkAsync(payload);
        await Send(client, Envelope(
            client,
            "job.completed",
            new JobCompleted(Result: result),
            jobId: jobId));
    }
    catch (Exception exc)
    {
        await Send(client, Envelope(
            client,
            "job.failed",
            new JobFailed(ErrorCode.Internal, exc.Message, Retryable: true),
            jobId: jobId));
    }
    finally
    {
        await stop.CancelAsync();
        try { await hb; } catch (OperationCanceledException) { }
    }
}

static async Task RunWorkerAsync(ARCPClient client)
{
    HashSet<Task> runners = [];
    await foreach (Env env in Events(client))
    {
        if (env.Type == "agent.delegate")
        {
            Task t = Task.Run(() => ExecuteAsync(client, env));
            runners.Add(t);
            _ = t.ContinueWith(x => runners.Remove(x), TaskScheduler.Default);
        }
        else if (env.Type == "session.evicted")
        {
            return;
        }
    }
}
