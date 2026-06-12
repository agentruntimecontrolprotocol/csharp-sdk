// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class EndToEndTests
{
    private static (ArcpServer server, MemoryTransport clientTransport) StartServer(System.Action<ArcpServer> configure)
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        configure(server);
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        return (server, client);
    }

    [Fact]
    public async Task Submit_and_stream_completes_with_result()
    {
        var (server, transport) = StartServer(s =>
            s.RegisterAgent("echo", async (ctx, ct) =>
            {
                await ctx.LogAsync("info", "hello", ct);
                return ctx.Input;
            }));

        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });
        client.SessionId.Value.Should().StartWith("sess_");
        client.EffectiveFeatures.Should().Contain(FeatureFlags.Heartbeat);

        var handle = await client.SubmitAsync("echo", new { hi = 1 });
        var result = await handle.Result.WaitAsync(System.TimeSpan.FromSeconds(5));
        result.Success.Should().BeTrue();
        result.Result!.FinalStatus.Should().Be("success");
    }

    [Fact]
    public async Task Agent_versioning_resolves_default()
    {
        var (server, transport) = StartServer(s =>
        {
            s.RegisterAgentVersion("code-refactor", "1.0.0", (ctx, ct) => Task.FromResult<object?>("v1"));
            s.RegisterAgentVersion("code-refactor", "2.0.0", (ctx, ct) => Task.FromResult<object?>("v2"));
            s.SetDefaultAgentVersion("code-refactor", "2.0.0");
        });

        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });
        var bareHandle = await client.SubmitAsync("code-refactor");
        bareHandle.Agent.Should().Be("code-refactor@2.0.0");
    }

    [Fact]
    public async Task Unknown_agent_version_returns_session_error()
    {
        var (_, transport) = StartServer(s =>
            s.RegisterAgentVersion("code-refactor", "1.0.0", (ctx, ct) => Task.FromResult<object?>(null)));

        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });

        // Submitting an unknown version is rejected with a session.error; the awaiting SubmitAsync
        // MUST surface that as a thrown ArcpException (it used to hang until cancellation).
        var act = async () => await client.SubmitAsync("code-refactor@9.9.9").WaitAsync(TimeSpan.FromSeconds(3));
        await act.Should().ThrowAsync<ArcpException>()
            .Where(e => e.Code == ErrorCode.AgentVersionNotAvailable);
    }

    [Fact]
    public async Task Progress_events_validate_and_stream()
    {
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("counter", async (ctx, ct) =>
            {
                for (long i = 1; i <= 3; i++) await ctx.ProgressAsync(i, total: 3, units: "items", message: $"step {i}", ct);
                return "done";
            }));

        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });
        var handle = await client.SubmitAsync("counter");

        var seen = 0;
        var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
        await foreach (var ev in handle.Events(cts.Token))
        {
            if (ev.Kind == "progress") seen++;
            if (seen == 3) break;
        }
        seen.Should().Be(3);
        await handle.Result.WaitAsync(System.TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Result_chunks_stream_and_terminate_in_job_result()
    {
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("chunker", async (ctx, ct) =>
            {
                var rid = ctx.BeginResultStream();
                await ctx.WriteChunkAsync(rid, "hello ", more: true, ct);
                await ctx.WriteChunkAsync(rid, "world", more: false, ct);
                return "summary";
            }));

        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });
        var handle = await client.SubmitAsync("chunker");
        var chunks = new System.Collections.Generic.List<ResultChunk>();
        var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
        await foreach (var c in handle.Chunks(cts.Token)) chunks.Add(c);
        var result = await handle.Result.WaitAsync(System.TimeSpan.FromSeconds(5));
        result.Success.Should().BeTrue();
        result.Result!.ResultId.Should().NotBeNullOrEmpty();
        string.Concat(chunks.Select(c => c.DecodedString)).Should().Be("hello world");
    }

    [Fact]
    public async Task Inbound_envelope_trace_id_is_propagated_to_job_accepted()
    {
        // Spec §11: ARCP propagates W3C trace context — the runtime must honor the inbound
        // envelope's trace_id rather than minting a fresh one.
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>(null)));
        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });

        // Send a raw job.submit with an explicit trace_id.
        const string expected = "0123456789abcdef0123456789abcdef";
        var traceCarrying = new Arcp.Core.Wire.Envelope
        {
            Type = MessageTypeNames.JobSubmit,
            SessionId = client.SessionId.Value,
            TraceId = expected,
            Payload = new JobSubmitPayload { Agent = "noop" },
        };
        await transport.SendAsync(traceCarrying);

        Arcp.Core.Wire.Envelope? accepted = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await foreach (var env in transport.ReceiveAsync(cts.Token))
            {
                if (env.Type == MessageTypeNames.JobAccepted) { accepted = env; break; }
            }
        }
        catch (OperationCanceledException) { }

        accepted.Should().NotBeNull();
        var payload = (JobAcceptedPayload)accepted!.Payload!;
        payload.TraceId.Should().Be(expected);
    }

    [Fact]
    public async Task ListJobs_returns_running_jobs()
    {
        var (_, transport) = StartServer(s =>
            s.RegisterAgent("sleeper", async (ctx, ct) =>
            {
                await Task.Delay(200, ct);
                return null;
            }));

        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
        });
        var handle = await client.SubmitAsync("sleeper");
        var page = await client.ListJobsAsync();
        page.Jobs.Should().NotBeEmpty();
        page.Jobs[0].Agent.Should().Be("sleeper");
    }
}
