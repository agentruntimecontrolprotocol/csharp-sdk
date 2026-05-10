using ARCP.Auth;
using ARCP.Errors;
using ARCP.Messages.Session;
using ARCP.Runtime;
using ARCP.Store;
using ARCP.Transport;
using FluentAssertions;
using Xunit;

namespace ARCP.IntegrationTests;

public class HandshakeTests
{
    private static (RuntimeIdentity Identity, Capabilities Caps, ClientIdentity Client, Capabilities ClientCaps) Defaults()
    {
        return (
            new RuntimeIdentity("arcp-test-runtime", "0.1.0"),
            new Capabilities
            {
                Streaming = true,
                HumanInput = true,
                Subscriptions = true,
                Anonymous = true,
                HeartbeatIntervalSeconds = 30,
                HeartbeatRecovery = "fail",
            },
            new ClientIdentity("arcp-test-client", "0.1.0", Principal: "tester@example"),
            new Capabilities { Streaming = true, HumanInput = true, Subscriptions = true });
    }

    private static async Task<(ARCPRuntime Runtime, EventLog Log, Task ServerLoop, MemoryTransport Client)> StartRuntime(
        IReadOnlyList<IAuthVerifier>? verifiers = null,
        Capabilities? capsOverride = null)
    {
        var defaults = Defaults();
        var log = await EventLog.OpenInMemoryAsync();
        var runtime = new ARCPRuntime(new ARCPRuntimeOptions
        {
            Identity = defaults.Identity,
            Capabilities = capsOverride ?? defaults.Caps,
            EventLog = log,
            AuthVerifiers = verifiers,
        });
        var (clientTransport, serverTransport) = MemoryTransport.CreatePair();
        var serverLoop = Task.Run(() => runtime.ServeAsync(serverTransport, CancellationToken.None));
        return (runtime, log, serverLoop, clientTransport);
    }

    [Fact]
    public async Task BearerHandshakeHappyPath()
    {
        BearerTokenStore store = (token, ct) => Task.FromResult<string?>(token == "good" ? "alice@x" : null);
        var (runtime, log, serverLoop, transport) = await StartRuntime(new[] { (IAuthVerifier)new BearerAuth(store) });
        await using var rt = runtime;
        await using var l = log;

        var defaults = Defaults();
        await using var client = await Client.ARCPClient.ConnectAsync(
            transport,
            new AuthCredential(AuthScheme.Bearer, "good"),
            defaults.Client,
            defaults.ClientCaps);

        client.SessionId.Should().NotBeNull();
        client.RuntimeIdentity!.Kind.Should().Be("arcp-test-runtime");
        client.NegotiatedCapabilities!.Streaming.Should().BeTrue();
        client.NegotiatedCapabilities.HeartbeatIntervalSeconds.Should().Be(30);

        await client.CloseAsync();
        await serverLoop;
    }

    [Fact]
    public async Task BearerHandshakeRejectsBadToken()
    {
        BearerTokenStore store = (token, ct) => Task.FromResult<string?>(token == "good" ? "alice@x" : null);
        var (runtime, log, serverLoop, transport) = await StartRuntime(new[] { (IAuthVerifier)new BearerAuth(store) });
        await using var rt = runtime;
        await using var l = log;

        var defaults = Defaults();
        Func<Task> act = async () => await Client.ARCPClient.ConnectAsync(
            transport,
            new AuthCredential(AuthScheme.Bearer, "bad"),
            defaults.Client,
            defaults.ClientCaps);

        await act.Should().ThrowAsync<ARCPException>().Where(e => e.Code == ErrorCode.Unauthenticated);
        await serverLoop;
    }

    [Fact]
    public async Task AnonymousAcceptedWhenCapabilityNegotiated()
    {
        var (runtime, log, serverLoop, transport) = await StartRuntime();
        await using var rt = runtime;
        await using var l = log;

        var defaults = Defaults();
        await using var client = await Client.ARCPClient.ConnectAsync(
            transport,
            new AuthCredential(AuthScheme.None),
            defaults.Client,
            new Capabilities { Streaming = true, Anonymous = true });

        client.SessionId.Should().NotBeNull();
        await client.CloseAsync();
        await serverLoop;
    }

    [Fact]
    public async Task AnonymousRejectedWithoutCapability()
    {
        var caps = new Capabilities
        {
            Streaming = true,
            HumanInput = true,
            Anonymous = false,
        };
        var (runtime, log, serverLoop, transport) = await StartRuntime(capsOverride: caps);
        await using var rt = runtime;
        await using var l = log;

        var defaults = Defaults();
        Func<Task> act = async () => await Client.ARCPClient.ConnectAsync(
            transport,
            new AuthCredential(AuthScheme.None),
            defaults.Client,
            defaults.ClientCaps);

        await act.Should().ThrowAsync<ARCPException>().Where(e => e.Code == ErrorCode.Unauthenticated);
        await serverLoop;
    }

    [Fact]
    public async Task MtlsAuthRejectedAsUnimplemented()
    {
        var (runtime, log, serverLoop, transport) = await StartRuntime();
        await using var rt = runtime;
        await using var l = log;

        var defaults = Defaults();
        Func<Task> act = async () => await Client.ARCPClient.ConnectAsync(
            transport,
            new AuthCredential(AuthScheme.Mtls),
            defaults.Client,
            defaults.ClientCaps);

        await act.Should().ThrowAsync<ARCPException>().Where(e => e.Code == ErrorCode.Unimplemented);
        await serverLoop;
    }

    [Fact]
    public async Task PingPongRoundTripsAfterHandshake()
    {
        BearerTokenStore store = (token, ct) => Task.FromResult<string?>("alice@x");
        var (runtime, log, serverLoop, transport) = await StartRuntime(new[] { (IAuthVerifier)new BearerAuth(store) });
        await using var rt = runtime;
        await using var l = log;

        var defaults = Defaults();
        await using var client = await Client.ARCPClient.ConnectAsync(
            transport,
            new AuthCredential(AuthScheme.Bearer, "tok"),
            defaults.Client,
            defaults.ClientCaps);

        DateTimeOffset received = await client.PingAsync();
        received.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(-5));

        await client.CloseAsync();
        await serverLoop;
    }
}
