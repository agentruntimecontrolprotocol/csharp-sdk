// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class JobContextEventsTests
{
    [Fact]
    public async Task All_event_kinds_round_trip_through_client()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            // This test exercises event round-tripping, not lease enforcement; allow uncovered
            // tool.call / agent.delegate operations (spec §9.3 deny-by-default is covered elsewhere).
            PermissiveUnleasedOperations = true,
        });
        server.RegisterAgent("emitter", async (ctx, ct) =>
        {
            await ctx.LogAsync("info", "hello", ct);
            await ctx.ThoughtAsync("thinking", ct);
            await ctx.StatusAsync("phase-1", "step", ct);
            await ctx.ProgressAsync(current: 1, total: 2, units: "items", message: "half", ct);
            await ctx.MetricAsync("perf.latency", 12.5, unit: "ms", cancellationToken: ct);
            await ctx.ToolCallAsync("calc", "tc1", new { op = "add" }, ct);
            await ctx.ToolResultAsync("tc1", new { value = 42 }, error: null, ct);
            await ctx.ArtifactRefAsync("s3://bucket/file.bin", contentType: "application/octet-stream", byteSize: 100, sha256: "abc", ct);
            await ctx.DelegateAsync("job_child", "agent-b", new { z = 1 }, ct);
            await ctx.EmitEventAsync("x-vendor.custom", new { tag = "y" }, ct);
            var rid = ctx.BeginResultStream();
            await ctx.WriteChunkAsync(rid, "part-A", more: true, ct);
            await ctx.WriteChunkAsync(rid, new byte[] { 1, 2, 3 }, more: false, ct);
            return "done";
        });
        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });
        var handle = await c.SubmitAsync("emitter");

        var kinds = new HashSet<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var ev in handle.Events(cts.Token))
        {
            kinds.Add(ev.Kind);
        }
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
        result.Success.Should().BeTrue();
        kinds.Should().Contain(new[] { "log", "thought", "status", "progress", "metric", "tool_call", "tool_result", "artifact_ref", "delegate", "result_chunk", "x-vendor.custom" });
    }

    [Fact]
    public async Task ProgressAsync_with_invalid_args_throws()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        Exception? caught = null;
        server.RegisterAgent("bad-progress", async (ctx, ct) =>
        {
            try
            {
                await ctx.ProgressAsync(current: 10, total: 5, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            return "done";
        });
        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });
        var handle = await c.SubmitAsync("bad-progress");
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));
        result.Success.Should().BeTrue();
        caught.Should().NotBeNull(because: "progress current > total must throw a validation error");
    }
}
