// JobMux: single reader on Events, fans out by job_id. Without this,
// parallel iterators on Events starve each other.
using System.Collections.Concurrent;
using ARCP.Client;
using ARCP.Ids;
using static ARCP.Samples.Delegation.ClientStubs;
using Env = ARCP.Envelope.Envelope;

namespace ARCP.Samples.Delegation;

public sealed record DelegatedJob(
    string Target,
    JobId? JobId = null,
    ARCP.Envelope.MessageType? Final = null,
    string? Error = null);

internal sealed class JobMux
{
    private readonly ARCPClient _client;
    private readonly HashSet<string> _terminal;
    private readonly ConcurrentDictionary<JobId, System.Threading.Channels.Channel<Env?>> _queues = new();
    private Task? _reader;

    public JobMux(ARCPClient client, HashSet<string> terminal)
    {
        _client = client;
        _terminal = terminal;
    }

    public void Start() => _reader = Task.Run(LoopAsync);

    public void Register(JobId jobId) =>
        _queues[jobId] = System.Threading.Channels.Channel.CreateUnbounded<Env?>();

    public async IAsyncEnumerable<Env> StreamAsync(JobId jobId)
    {
        var q = _queues[jobId];
        while (await q.Reader.ReadAsync() is { } env)
        {
            yield return env;
            if (_terminal.Contains(env.Type)) yield break;
        }
    }

    private async Task LoopAsync()
    {
        await foreach (Env env in Events(_client))
        {
            if (env.JobId is { } jid && _queues.TryGetValue(jid, out var q))
            {
                await q.Writer.WriteAsync(env);
                if (_terminal.Contains(env.Type))
                {
                    await q.Writer.WriteAsync(null);
                }
            }
        }
    }
}
