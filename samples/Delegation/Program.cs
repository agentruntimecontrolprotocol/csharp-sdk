// Fan a request out to peer runtimes; tolerate partial failure.
using System.Text.Json;
using ARCP.Client;
using ARCP.Ids;
using ARCP.Messages.Execution;
using ARCP.Samples.Delegation;
using static ARCP.Samples.Delegation.ClientStubs;
using Env = ARCP.Envelope.Envelope;

string[] peers = ["research.web", "research.code", "research.docs"];
HashSet<string> terminal = ["job.completed", "job.failed", "job.cancelled"];

static async Task<DelegatedJob> DelegateAsync(ARCPClient client, string target, string task, TraceId traceId)
{
    Env accepted = await Request(
        client,
        Envelope(
            client,
            "agent.delegate",
            new AgentDelegate(
                Target: target,
                Task: task,
                // trace_id propagates so peers join one distributed trace.
                Context: JsonSerializer.SerializeToElement(new { trace_id = traceId.Value })),
            traceId: traceId),
        timeout: TimeSpan.FromSeconds(10));
    if (accepted.Type != "job.accepted")
    {
        return new DelegatedJob(target, JobId: null, Error: $"got {accepted.Type}");
    }
    return new DelegatedJob(target, ((JobAccepted)accepted.Payload).JobId, Error: null);
}

static async Task<DelegatedJob> CollectAsync(JobMux mux, DelegatedJob job)
{
    if (job.Error is not null || job.JobId is null) return job;
    DelegatedJob current = job;
    await foreach (Env env in mux.StreamAsync(job.JobId.Value))
    {
        if (env.Type == "job.completed")
        {
            current = current with { Final = env.Payload };
        }
        else if (env.Type == "job.failed")
        {
            JobFailed failed = (JobFailed)env.Payload;
            current = current with { Error = $"{failed.Code}: {failed.Message}" };
        }
        else if (env.Type == "job.cancelled")
        {
            current = current with { Error = "CANCELLED" };
        }
    }
    return current;
}

ARCPClient client = null!; // transport, identity, auth elided
await Open(client);

JobMux mux = new(client, terminal);
mux.Start();

string request = "what changed in our auth stack in the last 30 days?";
TraceId traceId = new($"trace_{Guid.NewGuid():N}"[..18]);

List<DelegatedJob> jobs = [];
foreach (string peer in peers)
{
    DelegatedJob job = await DelegateAsync(client, peer, request, traceId);
    if (job.JobId is { } jid)
    {
        mux.Register(jid);
    }
    jobs.Add(job);
}

DelegatedJob[] completed = await Task.WhenAll(jobs.Select(j => CollectAsync(mux, j)));
Console.WriteLine(Synth.Synthesize(request, completed));

await client.CloseAsync();
