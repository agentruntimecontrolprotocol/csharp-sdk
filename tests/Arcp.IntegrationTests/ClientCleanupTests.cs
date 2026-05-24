// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class ClientCleanupTests
{
    /// <summary>Transport that throws on the second send. Used to assert that the client's
    /// pending-request bookkeeping does not retain stale entries after a failed send.</summary>
    private sealed class FlakySendTransport(ITransport inner, int throwAfter) : ITransport
    {
        private int _sends;

        public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref _sends);
            if (n > throwAfter)
                throw new IOException($"simulated send failure on call {n}");
            return inner.SendAsync(envelope, cancellationToken);
        }

        public IAsyncEnumerable<Envelope> ReceiveAsync(CancellationToken cancellationToken = default) =>
            inner.ReceiveAsync(cancellationToken);

        public ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default) =>
            inner.CloseAsync(reason, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Fact]
    public async Task ListJobsAsync_send_failure_does_not_leak_request_entry()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        server.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>(null));
        var (clientInner, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        // Allow only 1 send (the hello) — every subsequent send throws.
        var flaky = new FlakySendTransport(clientInner, throwAfter: 1);
        await using var c = await ArcpClient.ConnectAsync(flaky, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = new[] { FeatureFlags.ListJobs },
        });

        var act = async () => await c.ListJobsAsync();
        await act.Should().ThrowAsync<IOException>();

        // A second list_jobs attempt should not be poisoned by a stale entry — it should
        // also fail cleanly on send, not hang waiting for a never-coming response.
        var act2 = async () => await c.ListJobsAsync();
        await act2.Should().ThrowAsync<IOException>();
    }
}
