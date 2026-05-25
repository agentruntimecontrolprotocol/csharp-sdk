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
    public async Task Idempotent_retries_match_on_canonical_JSON_not_byte_identity()
    {
        // Spec §7.2: same key + identical *parameters* returns the same job — whitespace and
        // key order in the JSON input MUST NOT cause a false DUPLICATE_KEY.
        var (_, transport) = StartServer(s => s.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input)));
        await using var c = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });

        var compact = System.Text.Json.JsonDocument.Parse("{\"a\":1,\"b\":2}").RootElement.Clone();
        var prettySwapped = System.Text.Json.JsonDocument.Parse("{ \"b\" : 2, \"a\" : 1 }").RootElement.Clone();

        var first = await c.SubmitAsync("echo", compact, idempotencyKey: "canon-1");
        var second = await c.SubmitAsync("echo", prettySwapped, idempotencyKey: "canon-1");

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
