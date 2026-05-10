using System.Net;
using System.Net.WebSockets;
using ARCP.Auth;
using ARCP.Errors;
using ARCP.Messages.Session;
using ARCP.Store;
using ARCP.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using ARCPRuntime = ARCP.Runtime.ARCPRuntime;
using ARCPRuntimeOptions = ARCP.Runtime.ARCPRuntimeOptions;

namespace ARCP.IntegrationTests;

public class StdioTransportTests
{
    [Fact]
    public async Task HandshakeSucceedsOverPipedStdio()
    {
        // Build two paired anonymous pipes: client→server and server→client.
        var clientToServer = new System.IO.Pipelines.Pipe();
        var serverToClient = new System.IO.Pipelines.Pipe();
        var clientReader = new StreamReader(serverToClient.Reader.AsStream());
        var clientWriter = new StreamWriter(clientToServer.Writer.AsStream()) { AutoFlush = true };
        var serverReader = new StreamReader(clientToServer.Reader.AsStream());
        var serverWriter = new StreamWriter(serverToClient.Writer.AsStream()) { AutoFlush = true };

        await using var clientTransport = new StdioTransport(clientReader, clientWriter);
        await using var serverTransport = new StdioTransport(serverReader, serverWriter);

        await using var log = await EventLog.OpenInMemoryAsync();
        var runtime = new ARCPRuntime(new ARCPRuntimeOptions
        {
            Identity = new RuntimeIdentity("arcp-test-runtime", "0.1.0"),
            Capabilities = new Capabilities { Anonymous = true },
            EventLog = log,
        });

        Task serverLoop = Task.Run(() => runtime.ServeAsync(serverTransport, CancellationToken.None));

        await using var client = await Client.ARCPClient.ConnectAsync(
            clientTransport,
            new AuthCredential(AuthScheme.None),
            new ClientIdentity("arcp-test-client", "0.1.0"),
            new Capabilities { Anonymous = true });

        client.SessionId.Should().NotBeNull();
        await client.CloseAsync();
        await serverLoop;
    }
}

public class WebSocketTransportTests
{
    [Fact]
    public async Task HandshakeSucceedsOverRealLocalhostWebSocket()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var runtime = new ARCPRuntime(new ARCPRuntimeOptions
        {
            Identity = new RuntimeIdentity("arcp-test-runtime", "0.1.0"),
            Capabilities = new Capabilities { Anonymous = true },
            EventLog = log,
        });

        // Start an ephemeral-port WebSocket host.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(opts => opts.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.Map("/arcp", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            using WebSocket socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await using WebSocketTransport serverTransport = new(socket, ownsSocket: false);
            await runtime.ServeAsync(serverTransport, ctx.RequestAborted);
        });

        await app.StartAsync();
        try
        {
            string baseAddress = app.Urls.First();
            string wsUrl = baseAddress.Replace("http://", "ws://", StringComparison.Ordinal) + "/arcp";

            await using var clientTransport = await WebSocketTransport.ConnectAsync(new Uri(wsUrl));
            await using var client = await Client.ARCPClient.ConnectAsync(
                clientTransport,
                new AuthCredential(AuthScheme.None),
                new ClientIdentity("arcp-test-client", "0.1.0"),
                new Capabilities { Anonymous = true });

            client.SessionId.Should().NotBeNull();
            (await client.PingAsync()).Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(-5));
            await client.CloseAsync();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
