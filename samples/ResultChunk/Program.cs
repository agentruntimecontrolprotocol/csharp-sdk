// SPDX-License-Identifier: Apache-2.0
// samples/ResultChunk: agent streams a large result via result_chunk events; client reassembles
// via await foreach over JobHandle.Chunks(). Spec §8.4, §13.6.
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "result-chunk", Version = "1.0.0" },
});
server.RegisterAgent("report", async (ctx, ct) =>
{
    var rid = ctx.BeginResultStream();
    var lines = new[] { "Title\n", "Section A: ...\n", "Section B: ...\n", "Conclusion.\n" };
    for (var i = 0; i < lines.Length; i++)
    {
        await ctx.WriteChunkAsync(rid, lines[i], more: i < lines.Length - 1, ct);
        await Task.Delay(20, ct);
    }
    return $"Report generated, {lines.Length} chunks";
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "chunk-client", Version = "1.0.0" },
});
var handle = await client.SubmitAsync("report");
var assembly = new System.Text.StringBuilder();
await foreach (var chunk in handle.Chunks())
{
    Console.WriteLine($"  chunk {chunk.ChunkSeq} ({chunk.Encoding}, more={chunk.More})");
    assembly.Append(chunk.DecodedString);
}
var res = await handle.Result;
Console.WriteLine($"summary: {res.Result?.Summary}");
Console.WriteLine($"assembled:\n{assembly}");
return 0;
