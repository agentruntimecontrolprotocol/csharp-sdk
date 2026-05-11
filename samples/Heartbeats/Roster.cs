// In-memory worker registry. Real version probably backs onto Redis with
// per-worker TTLs.
using System.Text.Json;
using ARCP.Ids;

namespace ARCP.Samples.Heartbeats;

public sealed record WorkTask(
    string TaskId,
    string Role,
    JsonElement Payload,
    IdempotencyKey IdempotencyKey);

public sealed class Worker
{
    public Worker(string workerId, string role, DateTimeOffset lastHeartbeat)
    {
        WorkerId = workerId;
        Role = role;
        LastHeartbeat = lastHeartbeat;
    }

    public string WorkerId { get; }

    public string Role { get; }

    public DateTimeOffset LastHeartbeat { get; set; }

    public JobId? InFlightJob { get; set; }
}

public sealed class Roster
{
    public Dictionary<string, Worker> Workers { get; } = new();

    public Dictionary<string, List<string>> ByRole { get; } = new();

    public void Add(Worker w)
    {
        Workers[w.WorkerId] = w;
        if (!ByRole.TryGetValue(w.Role, out List<string>? list))
        {
            list = new();
            ByRole[w.Role] = list;
        }
        list.Add(w.WorkerId);
    }

    public void Remove(Worker w)
    {
        Workers.Remove(w.WorkerId);
        if (ByRole.TryGetValue(w.Role, out List<string>? list))
        {
            list.Remove(w.WorkerId);
        }
    }

    public List<Worker> Candidates(string role) =>
        (ByRole.TryGetValue(role, out List<string>? list) ? list : [])
            .Select(id => Workers[id])
            .Where(w => w.InFlightJob is null)
            .ToList();
}
