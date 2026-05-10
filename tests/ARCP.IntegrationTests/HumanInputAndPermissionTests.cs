using System.Text.Json;
using System.Threading.Channels;
using ARCP.Auth;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Execution;
using ARCP.Messages.Human;
using ARCP.Messages.Permissions;
using ARCP.Messages.Session;
using ARCP.Runtime;
using ARCP.Store;
using ARCP.Transport;
using FluentAssertions;
using Xunit;

namespace ARCP.IntegrationTests;

public class HumanInputAndPermissionTests
{
    private static readonly RuntimeIdentity Identity = new("arcp-test-runtime", "0.1.0");
    private static readonly Capabilities ServerCaps = new()
    {
        DurableJobs = true,
        Streaming = true,
        HumanInput = true,
        Anonymous = true,
        HeartbeatIntervalSeconds = 30,
        HeartbeatRecovery = "fail",
    };
    private static readonly ClientIdentity Client = new("arcp-test-client", "0.1.0");

    private sealed class Rig : IAsyncDisposable
    {
        public required ARCPRuntime Runtime { get; init; }

        public required EventLog Log { get; init; }

        public required JobManager Jobs { get; init; }

        public required Task ServerLoop { get; init; }

        public required MemoryTransport ClientTransport { get; init; }

        public required Ids.SessionId SessionId { get; init; }

        public required Channel<Envelope.Envelope> Inbound { get; init; }

        public async Task SendAsync(MessageType payload, MessageId? id = null, MessageId? correlationId = null)
        {
            var env = new Envelope.Envelope
            {
                Arcp = ProtocolVersion.Wire,
                Id = id ?? MessageId.New(),
                Type = payload.WireType,
                Timestamp = DateTimeOffset.UtcNow,
                Payload = payload,
                SessionId = SessionId,
                CorrelationId = correlationId,
            };
            await ClientTransport.SendAsync(new WireFrame(JsonSerializer.Serialize(env, EnvelopeJson.Options)));
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
            throw new TimeoutException($"Did not observe {typeof(T).Name}.");
        }

        public async ValueTask DisposeAsync()
        {
            try { await ClientTransport.DisposeAsync(); } catch { /* best effort */ }
            try { await Jobs.DisposeAsync(); } catch { /* best effort */ }
            try { await Runtime.DisposeAsync(); } catch { /* best effort */ }
            try { await Log.DisposeAsync(); } catch { /* best effort */ }
        }
    }

    private static async Task<Rig> SetupAsync(IReadOnlyDictionary<string, ToolHandler> tools)
    {
        EventLog log = await EventLog.OpenInMemoryAsync();
        var (clientTransport, serverTransport) = MemoryTransport.CreatePair();

        async ValueTask Emit(Envelope.Envelope env, CancellationToken ct)
        {
            string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);
            await serverTransport.SendAsync(new WireFrame(json), ct);
        }

        JobManager jobs = new(tools, Emit);
        ARCPRuntime runtime = new(new ARCPRuntimeOptions
        {
            Identity = Identity,
            Capabilities = ServerCaps,
            EventLog = log,
            JobManager = jobs,
        });

        Task serverLoop = Task.Run(() => runtime.ServeAsync(serverTransport, CancellationToken.None));

        Channel<Envelope.Envelope> inbound = Channel.CreateUnbounded<Envelope.Envelope>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var env in EnvelopeReader.ReceiveAsync(clientTransport, EnvelopeJson.Options, CancellationToken.None))
                {
                    await inbound.Writer.WriteAsync(env);
                }
            }
            catch
            {
            }
            finally
            {
                inbound.Writer.TryComplete();
            }
        });

        // Drive handshake.
        await clientTransport.SendAsync(new WireFrame(JsonSerializer.Serialize(new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "session.open",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new SessionOpen(
                new AuthCredential(AuthScheme.None),
                Client,
                new Capabilities { Anonymous = true, DurableJobs = true, HumanInput = true }),
        }, EnvelopeJson.Options)));

        Envelope.Envelope acceptedEnv;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            acceptedEnv = await inbound.Reader.ReadAsync(cts.Token);
        }
        var accepted = (SessionAccepted)acceptedEnv.Payload;

        return new Rig
        {
            Runtime = runtime,
            Log = log,
            Jobs = jobs,
            ServerLoop = serverLoop,
            ClientTransport = clientTransport,
            SessionId = accepted.SessionId,
            Inbound = inbound,
        };
    }

    [Fact]
    public async Task HumanInputRoundTripCompletesJob()
    {
        ToolHandler interactive = async (invoke, ctx, ct) =>
        {
            JsonElement schema = JsonDocument.Parse("""{"type":"object","properties":{"branch":{"type":"string"}}}""").RootElement;
            JsonElement value = await ctx.RequestInputAsync(
                "Branch?",
                schema,
                DateTimeOffset.UtcNow.AddMinutes(1));
            return new ToolResult(Value: value);
        };
        await using Rig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["choose"] = interactive });

        await rig.SendAsync(new ToolInvoke("choose"));

        // Wait for human.input.request and capture its id so we can correlate the response.
        Envelope.Envelope reqEnv = await rig.ExpectAsync<HumanInputRequest>();
        await rig.SendAsync(
            new HumanInputResponse(
                Value: JsonDocument.Parse("""{"branch":"fix/nice"}""").RootElement,
                RespondedBy: "test",
                RespondedAt: DateTimeOffset.UtcNow),
            correlationId: reqEnv.Id);

        Envelope.Envelope completed = await rig.ExpectAsync<JobCompleted>();
        var c = (JobCompleted)completed.Payload;
        c.Result.Should().NotBeNull();
        c.Result!.Value.GetProperty("branch").GetString().Should().Be("fix/nice");
    }

    [Fact]
    public async Task HumanInputDeadlineExceededFallsBackToDefault()
    {
        ToolHandler interactive = async (invoke, ctx, ct) =>
        {
            JsonElement schema = JsonDocument.Parse("""{"type":"object"}""").RootElement;
            JsonElement def = JsonDocument.Parse("""{"branch":"default"}""").RootElement;
            JsonElement value = await ctx.RequestInputAsync(
                "Branch?",
                schema,
                DateTimeOffset.UtcNow.AddMilliseconds(150),
                @default: def);
            return new ToolResult(Value: value);
        };
        await using Rig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["choose"] = interactive });

        await rig.SendAsync(new ToolInvoke("choose"));

        // Don't respond — deadline triggers default.
        Envelope.Envelope completed = await rig.ExpectAsync<JobCompleted>(TimeSpan.FromSeconds(5));
        var c = (JobCompleted)completed.Payload;
        c.Result!.Value.GetProperty("branch").GetString().Should().Be("default");
    }

    [Fact]
    public async Task PermissionGrantMintsLeaseAndJobCompletes()
    {
        ToolHandler protectedTool = async (invoke, ctx, ct) =>
        {
            await ctx.RequestPermissionAsync("filesystem.write", "/etc/hosts", "write", "test", 60);
            return new ToolResult(Value: JsonDocument.Parse("\"granted\"").RootElement);
        };
        await using Rig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["protected"] = protectedTool });

        await rig.SendAsync(new ToolInvoke("protected"));

        Envelope.Envelope reqEnv = await rig.ExpectAsync<PermissionRequest>();
        await rig.SendAsync(
            new PermissionGrant("filesystem.write", "/etc/hosts", "write", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(1)),
            correlationId: reqEnv.Id);

        await rig.ExpectAsync<LeaseGranted>();
        await rig.ExpectAsync<JobCompleted>();
    }

    [Fact]
    public async Task PermissionDenyFailsJob()
    {
        ToolHandler protectedTool = async (invoke, ctx, ct) =>
        {
            await ctx.RequestPermissionAsync("filesystem.write", "/etc/hosts", "write");
            return new ToolResult();
        };
        await using Rig rig = await SetupAsync(new Dictionary<string, ToolHandler> { ["protected"] = protectedTool });

        await rig.SendAsync(new ToolInvoke("protected"));

        Envelope.Envelope reqEnv = await rig.ExpectAsync<PermissionRequest>();
        await rig.SendAsync(
            new PermissionDeny("filesystem.write", "/etc/hosts", "write", "policy"),
            correlationId: reqEnv.Id);

        Envelope.Envelope failed = await rig.ExpectAsync<JobFailed>();
        ((JobFailed)failed.Payload).Code.Should().Be(ErrorCode.PermissionDenied);
    }
}

public class LeaseManagerTests
{
    [Fact]
    public void IssueAndCheckHappyPath()
    {
        var lm = new LeaseManager();
        var grant = lm.Issue("p", "r", "o", TimeSpan.FromMinutes(1));
        lm.Check(grant.LeaseId, "p", "r", "o");
    }

    [Fact]
    public void RevokedLeaseThrows()
    {
        var lm = new LeaseManager();
        var grant = lm.Issue("p", "r", "o", TimeSpan.FromMinutes(1));
        lm.Revoke(grant.LeaseId, "policy");
        Action act = () => lm.Check(grant.LeaseId, "p", "r", "o");
        act.Should().Throw<LeaseRevokedException>();
    }

    [Fact]
    public void ExpiredLeaseThrows()
    {
        var fake = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        var lm = new LeaseManager(fake);
        var grant = lm.Issue("p", "r", "o", TimeSpan.FromMilliseconds(100));
        fake.Advance(TimeSpan.FromSeconds(1));
        Action act = () => lm.Check(grant.LeaseId, "p", "r", "o");
        act.Should().Throw<LeaseExpiredException>();
    }

    [Fact]
    public void MismatchedScopeIsDenied()
    {
        var lm = new LeaseManager();
        var grant = lm.Issue("p", "r", "o", TimeSpan.FromMinutes(1));
        Action act = () => lm.Check(grant.LeaseId, "different", "r", "o");
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void ExtendBumpsExpiry()
    {
        var fake = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        var lm = new LeaseManager(fake);
        var grant = lm.Issue("p", "r", "o", TimeSpan.FromSeconds(1));
        fake.Advance(TimeSpan.FromMilliseconds(500));
        var extended = lm.Extend(grant.LeaseId, TimeSpan.FromSeconds(10));
        extended.ExpiresAt.Should().BeAfter(grant.ExpiresAt);
    }
}
