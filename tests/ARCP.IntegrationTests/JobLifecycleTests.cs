using System.Text.Json;
using System.Threading.Channels;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Control;
using ARCP.Messages.Execution;
using ARCP.Messages.Session;
using ARCP.Runtime;
using ARCP.Store;
using ARCP.Transport;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ARCP.IntegrationTests;

public class JobLifecycleTests
{
    private static (RuntimeIdentity Identity, Capabilities Caps, ClientIdentity Client) Defaults() =>
        (new RuntimeIdentity("arcp-test-runtime", "0.1.0"),
         new Capabilities { Streaming = true, DurableJobs = true, Anonymous = true, HeartbeatIntervalSeconds = 30, HeartbeatRecovery = "fail" },
         new ClientIdentity("arcp-test-client", "0.1.0"));

    /// <summary>
    /// Wire-level test rig. We drive the handshake by hand via the
    /// MemoryTransport pair so the test owns both ends of the channel and
    /// nothing else competes for inbound reads.
    /// </summary>
    private sealed class WireRig : IAsyncDisposable
    {
        public required ARCPRuntime Runtime { get; init; }

        public required EventLog Log { get; init; }

        public required JobManager Jobs { get; init; }

        public required Task ServerLoop { get; init; }

        public required MemoryTransport ClientTransport { get; init; }

        public required Ids.SessionId SessionId { get; init; }

        public required Channel<Envelope.Envelope> Inbound { get; init; }

        public required Task InboundReader { get; init; }

        public async ValueTask DisposeAsync()
        {
            try { await ClientTransport.DisposeAsync(); } catch { /* best effort */ }
            try { await Jobs.DisposeAsync(); } catch { /* best effort */ }
            try { await Runtime.DisposeAsync(); } catch { /* best effort */ }
            try { await Log.DisposeAsync(); } catch { /* best effort */ }
        }

        public async Task SendAsync(MessageType payload, MessageId? id = null)
        {
            string wireType = payload.WireType;
            var env = new Envelope.Envelope
            {
                Arcp = ProtocolVersion.Wire,
                Id = id ?? MessageId.New(),
                Type = wireType,
                Timestamp = DateTimeOffset.UtcNow,
                Payload = payload,
                SessionId = SessionId,
            };
            string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);
            await ClientTransport.SendAsync(new WireFrame(json), CancellationToken.None);
        }

        public async Task<Envelope.Envelope> ExpectAsync<T>(TimeSpan? timeout = null)
            where T : MessageType
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
            await foreach (var env in Inbound.Reader.ReadAllAsync(cts.Token))
            {
                if (env.Payload is T)
                {
                    return env;
                }
            }
            throw new TimeoutException($"Did not observe envelope of type {typeof(T).Name} within timeout.");
        }
    }

    private static async Task<WireRig> SetupAsync(
        IReadOnlyDictionary<string, ToolHandler> tools,
        TimeProvider? time = null,
        string heartbeatRecovery = "fail",
        int heartbeatIntervalSeconds = 30)
    {
        var defaults = Defaults();
        EventLog log = await EventLog.OpenInMemoryAsync();
        var (clientTransport, serverTransport) = MemoryTransport.CreatePair();

        async ValueTask Emit(Envelope.Envelope env, CancellationToken ct)
        {
            string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);
            await serverTransport.SendAsync(new WireFrame(json), ct).ConfigureAwait(false);
        }

        JobManager jobs = new(tools, Emit, heartbeatIntervalSeconds, heartbeatRecovery, time);

        ARCPRuntime runtime = new(new ARCPRuntimeOptions
        {
            Identity = defaults.Identity,
            Capabilities = defaults.Caps with { HeartbeatIntervalSeconds = heartbeatIntervalSeconds, HeartbeatRecovery = heartbeatRecovery },
            EventLog = log,
            JobManager = jobs,
        });

        Task serverLoop = Task.Run(() => runtime.ServeAsync(serverTransport, CancellationToken.None));

        // Pump inbound frames into a Channel so multiple ExpectAsync calls don't compete.
        Channel<Envelope.Envelope> inbound = Channel.CreateUnbounded<Envelope.Envelope>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        Task reader = Task.Run(async () =>
        {
            try
            {
                await foreach (var env in EnvelopeReader.ReceiveAsync(clientTransport, EnvelopeJson.Options, CancellationToken.None))
                {
                    await inbound.Writer.WriteAsync(env);
                }
            }
            catch (Exception)
            {
                // shutdown
            }
            finally
            {
                inbound.Writer.TryComplete();
            }
        });

        // Hand-drive the handshake.
        MessageId openId = MessageId.New();
        var openEnv = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = openId,
            Type = "session.open",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new SessionOpen(
                new AuthCredential(AuthScheme.None),
                defaults.Client,
                new Capabilities { DurableJobs = true, Streaming = true, Anonymous = true }),
        };
        await clientTransport.SendAsync(new WireFrame(JsonSerializer.Serialize(openEnv, EnvelopeJson.Options)));

        // Read the accepted envelope synchronously through the channel.
        Envelope.Envelope acceptedEnv;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            acceptedEnv = await inbound.Reader.ReadAsync(cts.Token);
        }
        if (acceptedEnv.Payload is not SessionAccepted accepted)
        {
            throw new InvalidOperationException($"Expected session.accepted, got {acceptedEnv.Type}.");
        }

        return new WireRig
        {
            Runtime = runtime,
            Log = log,
            Jobs = jobs,
            ServerLoop = serverLoop,
            ClientTransport = clientTransport,
            SessionId = accepted.SessionId,
            Inbound = inbound,
            InboundReader = reader,
        };
    }

    [Fact]
    public async Task ToolHappyPathEmitsAcceptedStartedCompleted()
    {
        ToolHandler echo = (invoke, ctx, ct) =>
            Task.FromResult(new ToolResult(Value: JsonDocument.Parse("\"ok\"").RootElement));
        await using WireRig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["echo"] = echo });

        await rig.SendAsync(new ToolInvoke("echo"));

        var seq = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var env = await rig.Inbound.Reader.ReadAsync(cts.Token);
            seq.Add(env.Type);
            if (env.Type == "job.completed")
            {
                break;
            }
        }
        seq.Should().ContainInOrder("job.accepted", "job.started", "job.completed");
    }

    [Fact]
    public async Task ToolFailurePropagatesAsJobFailed()
    {
        ToolHandler failing = (invoke, ctx, ct) =>
            throw new InvalidArgumentException("bad input");
        await using WireRig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["bad"] = failing });

        await rig.SendAsync(new ToolInvoke("bad"));

        var failed = await rig.ExpectAsync<JobFailed>();
        var f = (JobFailed)failed.Payload;
        f.Code.Should().Be(ErrorCode.InvalidArgument);
        f.Message.Should().Contain("bad input");
    }

    [Fact]
    public async Task UnknownToolNackedAsNotFound()
    {
        await using WireRig rig = await SetupAsync(new Dictionary<string, ToolHandler>());

        await rig.SendAsync(new ToolInvoke("missing"));

        var nack = await rig.ExpectAsync<Nack>();
        ((Nack)nack.Payload).Code.Should().Be(ErrorCode.NotFound);
    }

    [Fact]
    public async Task CancelOfRunningJobEmitsAcceptedThenCancelled()
    {
        var release = new TaskCompletionSource<bool>();
        ToolHandler longRunning = async (invoke, ctx, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return new ToolResult();
        };
        await using WireRig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["long"] = longRunning });

        await rig.SendAsync(new ToolInvoke("long"));
        var accepted = await rig.ExpectAsync<JobAccepted>();
        var jobId = ((JobAccepted)accepted.Payload).JobId;

        await rig.SendAsync(new Cancel(CancelTarget.Job, jobId.Value, Reason: "user_aborted"));
        await rig.ExpectAsync<CancelAccepted>();
        await rig.ExpectAsync<JobCancelled>();
    }

    [Fact]
    public async Task CancelOfUnknownJobReturnsCancelRefusedNotFound()
    {
        await using WireRig rig = await SetupAsync(new Dictionary<string, ToolHandler>());

        await rig.SendAsync(new Cancel(CancelTarget.Job, "job_unknown"));
        var refused = await rig.ExpectAsync<CancelRefused>();
        ((CancelRefused)refused.Payload).Reason.Should().Be(CancelRefusedReason.NotFound);
    }

    [Fact]
    public async Task HeartbeatLostUnderFailRecoveryEmitsJobFailed()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        var release = new TaskCompletionSource<bool>();
        ToolHandler stalling = async (invoke, ctx, ct) =>
        {
            await ctx.HeartbeatAsync(deadlineMs: 100);
            await release.Task.WaitAsync(ct);
            return new ToolResult();
        };

        await using WireRig rig = await SetupAsync(
            new Dictionary<string, ToolHandler> { ["stall"] = stalling },
            time: time,
            heartbeatRecovery: "fail",
            heartbeatIntervalSeconds: 1);

        await rig.SendAsync(new ToolInvoke("stall"));

        // Drain accepted/started/heartbeat envelopes until we see job.failed.
        await Task.Delay(100);
        time.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        time.Advance(TimeSpan.FromSeconds(3));

        var failed = await rig.ExpectAsync<JobFailed>(TimeSpan.FromSeconds(10));
        var f = (JobFailed)failed.Payload;
        f.Code.Should().Be(ErrorCode.HeartbeatLost);
        release.TrySetResult(true);
    }
}
