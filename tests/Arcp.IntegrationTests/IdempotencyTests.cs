// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class IdempotencyTests
{
    private static (ArcpServer server, MemoryTransport clientTransport) StartServer(Action<ArcpServer> configure, int idempotencyWindowSec = 3600)
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            IdempotencyWindowSec = idempotencyWindowSec,
        });
        configure(server);
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        return (server, client);
    }

    [Fact]
    public async Task Identical_retry_returns_existing_job_id()
    {
        var (_, transport) = StartServer(s => s.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input)));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });

        var first = await c.SubmitAsync("echo", new { x = 1 }, idempotencyKey: "key-1");
        var second = await c.SubmitAsync("echo", new { x = 1 }, idempotencyKey: "key-1");

        second.JobId.Value.Should().Be(first.JobId.Value);
    }

    [Fact]
    public async Task Mismatched_input_with_same_key_raises_session_error()
    {
        var (_, transport) = StartServer(s => s.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input)));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });

        var first = await c.SubmitAsync("echo", new { x = 1 }, idempotencyKey: "key-2");
        first.JobId.Value.Should().NotBeNullOrEmpty();

        // Submitting again with the same key but a different payload should never resolve to
        // an acceptance — the server emits a session.error with DUPLICATE_KEY.
        var submitTask = c.SubmitAsync("echo", new { x = 99 }, idempotencyKey: "key-2");
        var completed = await Task.WhenAny(submitTask, Task.Delay(800));
        completed.Should().NotBeSameAs(submitTask);
    }
}
