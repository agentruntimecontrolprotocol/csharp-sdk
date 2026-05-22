// SPDX-License-Identifier: Apache-2.0
// recipes/stream-resume: demonstrates EventLog replay (spec §14, §5.3) after a
// mid-stream disconnect.  Three independent sessions are used:
//   1. Submitter  — owns the job and keeps it alive regardless of observer lifetimes.
//   2. Observer-1 — subscribes live, reads 3 chunks, then disconnects abruptly.
//   3. Observer-2 — reconnects with the first observer's ResumeToken and calls
//                   SubscribeAsync(history: true) to replay every chunk from the
//                   beginning, recovering the missed portion of the stream.
using System.Text;
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

using var cts = new CancellationTokenSource();
var ct = cts.Token;

// ── server ────────────────────────────────────────────────────────────────────
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "stream-resume", Version = "1.0.0" },
});

// Writer emits 8 chunks with a short delay between each so the observers can
// experience genuine partial delivery.
var lines = Enumerable.Range(1, 8)
                      .Select(n => $"chunk-{n:00}: synthetic payload line {n}\n")
                      .ToArray();

server.RegisterAgent("writer", async (ctx, wct) =>
{
    var rid = ctx.BeginResultStream();
    for (var i = 0; i < lines.Length; i++)
    {
        await Task.Delay(50, wct);
        await ctx.WriteChunkAsync(rid, lines[i], more: i < lines.Length - 1, wct);
    }
    return new { chunks = lines.Length };
});

// ── session 1: submitter ──────────────────────────────────────────────────────
// This session owns the job.  Because the server associates job lifetime with the
// submitting session, keeping this session open lets the writer run to completion
// even after observer-1 disconnects.
var (subT, subServerT) = MemoryTransport.Pair();
_ = server.AcceptAsync(subServerT, ct);
await using var submitter = await ArcpClient.ConnectAsync(subT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "submitter", Version = "1.0.0" },
});

var jobHandle = await submitter.SubmitAsync("writer", cancellationToken: ct);
var jobId = jobHandle.JobId;
Console.WriteLine($"submitted job {jobId}");

// ── session 2: observer-1 ─────────────────────────────────────────────────────
// Subscribes to the live stream, reads exactly 3 chunks, then breaks and
// disposes — simulating a client crash or network drop mid-stream.
var (obs1T, obs1ServerT) = MemoryTransport.Pair();
_ = server.AcceptAsync(obs1ServerT, ct);
var obs1 = await ArcpClient.ConnectAsync(obs1T, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "observer-1", Version = "1.0.0" },
});

var obs1Handle = await obs1.SubscribeAsync(jobId, history: false, cancellationToken: ct);
var seenChunks = 0;
await foreach (var chunk in obs1Handle.Chunks())
{
    Console.WriteLine($"obs1 ← seq {chunk.ChunkSeq}: {chunk.DecodedString.TrimEnd()}");
    if (++seenChunks >= 3) break;
}

// Capture the ResumeToken before the connection closes — it lets observer-2
// authenticate with the same identity and access the session's EventLog.
var savedToken = obs1.ResumeToken;
Console.WriteLine($"obs1 disconnecting — ResumeToken: {savedToken?[..Math.Min(12, savedToken?.Length ?? 0)]}…");
await obs1.DisposeAsync();

// ── wait for the writer to finish ─────────────────────────────────────────────
// The submitter session is still alive, so the writer runs to completion.
var finalResult = await jobHandle.Result;
Console.WriteLine($"writer finished — success: {finalResult.Success}");

// ── session 3: observer-2 (replay) ────────────────────────────────────────────
// Reconnects with the saved token, then calls SubscribeAsync with history:true.
// The server replays the full EventLog for this job, including the 5 chunks
// that observer-1 missed.
var (obs2T, obs2ServerT) = MemoryTransport.Pair();
_ = server.AcceptAsync(obs2ServerT, ct);
await using var obs2 = await ArcpClient.ConnectAsync(obs2T, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "observer-2", Version = "1.0.0" },
    ResumeToken = savedToken,
});

var obs2Handle = await obs2.SubscribeAsync(jobId, history: true, cancellationToken: ct);
var assembly = new StringBuilder();
var totalReplayed = 0;
await foreach (var chunk in obs2Handle.Chunks())
{
    assembly.Append(chunk.DecodedString);
    totalReplayed++;
}

Console.WriteLine($"obs2 replayed {totalReplayed} chunks (expected {lines.Length})");
Console.WriteLine($"reassembled output:\n{assembly}");
return 0;
