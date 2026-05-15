// SPDX-License-Identifier: Apache-2.0
using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Arcp.AspNetCore;
using Arcp.Client;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Arcp.AspNetCore.Tests;

public class MapArcpTests
{
    [Fact]
    public async Task MapArcp_completes_websocket_round_trip_with_test_host()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test", Version = "1.0.0" },
        });
        server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>("ok"));

        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddSingleton(server);
                s.AddRouting();
            });
            web.Configure(app =>
            {
                app.UseWebSockets();
                app.UseRouting();
                app.UseEndpoints(e => e.MapArcp(server));
            });
        });
        using var host = await builder.StartAsync();
        var testServer = host.GetTestServer();
        var wsClient = testServer.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri(testServer.BaseAddress, "arcp"), CancellationToken.None);
        var transport = new WebSocketTransport(socket, ownsSocket: false);
        await using var arcpClient = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "ws-test", Version = "1.0.0" },
        });
        arcpClient.Runtime!.Name.Should().Be("test");
        var handle = await arcpClient.SubmitAsync("echo");
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
        result.Success.Should().BeTrue();
    }
}
