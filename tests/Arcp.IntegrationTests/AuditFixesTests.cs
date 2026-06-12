// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Arcp.Runtime;
using Arcp.Runtime.Authorization;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class AuditFixesTests
{
    private static (ArcpServer server, MemoryTransport clientT) StartServer(Action<ArcpServer> configure,
        IBearerVerifier? auth = null, IJobAuthorizationPolicy? policy = null, TimeProvider? time = null)
    {
        var opts = new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            Auth = auth,
            AuthorizationPolicy = policy ?? new SamePrincipalPolicy(),
            TimeProvider = time ?? TimeProvider.System,
        };
        var server = new ArcpServer(opts);
        configure(server);
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        return (server, client);
    }

    // ── #41: session.close / session.closed wire types (spec §6.7) ──────────────────────────────
    [Fact]
    public void SessionClose_and_SessionClosed_are_registered_wire_types()
    {
        MessageTypeNames.All.Should().Contain(new[] { MessageTypeNames.SessionClose, MessageTypeNames.SessionClosed });
        MessageTypeRegistry.Default.TryGet(MessageTypeNames.SessionClose, out _).Should().BeTrue();
        MessageTypeRegistry.Default.TryGet(MessageTypeNames.SessionClosed, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Runtime_accepts_session_close_and_replies_session_closed()
    {
        var (_, t) = StartServer(s => s.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>(null)));
        await t.SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionHello,
            Payload = new SessionHelloPayload
            {
                Client = new ClientInfo { Name = "t", Version = "1" },
                Capabilities = new Capabilities { Encodings = new[] { "json" }, Features = Array.Empty<string>() },
            },
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Envelope? closed = null;
        await foreach (var env in t.ReceiveAsync(cts.Token))
        {
            if (env.Type == MessageTypeNames.SessionWelcome)
            {
                await t.SendAsync(new Envelope
                {
                    Type = MessageTypeNames.SessionClose,
                    SessionId = env.SessionId,
                    Payload = new SessionByePayload { Reason = "done" },
                });
            }
            else if (env.Type == MessageTypeNames.SessionClosed)
            {
                closed = env;
                break;
            }
        }

        closed.Should().NotBeNull("the runtime must acknowledge session.close with session.closed");
    }

    // ── #46: model.use advertised independently of credential provisioning (spec §9.7) ──────────
    [Fact]
    public async Task ModelUse_is_advertised_without_a_credential_provisioner()
    {
        var (_, t) = StartServer(s => s.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>(null)));
        await using var c = await ArcpClient.ConnectAsync(t, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });
        c.EffectiveFeatures.Should().Contain(FeatureFlags.ModelUse);
    }

    // ── #40: unexpected dispatch exception surfaces INTERNAL_ERROR (spec §12) ────────────────────
    private sealed class ThrowingPolicy : IJobAuthorizationPolicy
    {
        public bool CanObserve(string? jobSubmitterPrincipal, AuthPrincipal? requestor) =>
            throw new InvalidOperationException("boom");
    }

    // ── #45 + #40 with two principals on one runtime ────────────────────────────────────────────
    [Fact]
    public async Task Empty_principal_sees_no_jobs_and_throwing_policy_surfaces_INTERNAL_ERROR()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            Auth = new MappingVerifier(),
            AuthorizationPolicy = new SamePrincipalPolicy(),
        });
        server.RegisterAgent("sleeper", async (ctx, ct) => { await Task.Delay(3000, ct); return null; });

        var (aliceT, aliceSrv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(aliceSrv));
        await using var alice = await ArcpClient.ConnectAsync(aliceT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "alice", Version = "1" },
            Token = "alice",
        });
        await alice.SubmitAsync("sleeper");

        // alice sees her own job.
        (await alice.ListJobsAsync()).Jobs.Should().ContainSingle();

        // A session whose principal subject is empty must see nothing (fail-closed, spec §6.6/§14).
        var (ghostT, ghostSrv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(ghostSrv));
        await using var ghost = await ArcpClient.ConnectAsync(ghostT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "ghost", Version = "1" },
            Token = "ghost", // MappingVerifier maps this to an EMPTY subject
        });
        (await ghost.ListJobsAsync()).Jobs.Should().BeEmpty();
    }

    private sealed class MappingVerifier : IBearerVerifier
    {
        public ValueTask<AuthPrincipal?> VerifyAsync(string? token, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuthPrincipal?>(token switch
            {
                "ghost" => new AuthPrincipal(string.Empty),
                null or "" => null,
                _ => new AuthPrincipal(token),
            });
    }

    [Fact]
    public async Task ListJobs_rejected_by_throwing_policy_surfaces_INTERNAL_ERROR()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            Auth = new AllowAnyBearerVerifier(),
            AuthorizationPolicy = new ThrowingPolicy(),
        });
        server.RegisterAgent("sleeper", async (ctx, ct) => { await Task.Delay(3000, ct); return null; });

        var (aliceT, aliceSrv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(aliceSrv));
        await using var alice = await ArcpClient.ConnectAsync(aliceT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "alice", Version = "1" }, Token = "alice",
        });
        await alice.SubmitAsync("sleeper");

        var (bobT, bobSrv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(bobSrv));
        await using var bob = await ArcpClient.ConnectAsync(bobT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "bob", Version = "1" }, Token = "bob",
        });

        // bob listing forces ThrowingPolicy.CanObserve over alice's job → unexpected exception →
        // session.error{INTERNAL_ERROR}; #73 makes the awaiting ListJobsAsync throw it.
        var act = async () => await bob.ListJobsAsync().WaitAsync(TimeSpan.FromSeconds(3));
        (await act.Should().ThrowAsync<ArcpException>()).Which.Code.Should().Be(ErrorCode.InternalError);
    }

    // ── #37: a job survives session teardown and is not cancelled (spec §6.4, §6.7) ──────────────
    [Fact]
    public async Task Job_keeps_running_after_its_session_transport_drops()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (_, t) = StartServer(s => s.RegisterAgent("long", async (ctx, ct) =>
        {
            started.TrySetResult();
            // Honors the job token: if the job were cancelled on session close this would throw.
            await release.Task.WaitAsync(ct);
            finished.TrySetResult();
            return "ok";
        }));

        var c = await ArcpClient.ConnectAsync(t, new ArcpClientOptions { Client = new ClientInfo { Name = "t", Version = "1" } });
        await c.SubmitAsync("long");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Drop the session.
        await c.DisposeAsync();

        // The job must still be alive: releasing it now lets it complete (it would have thrown on a
        // cancelled token if session teardown had terminated it).
        release.TrySetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    // ── #43: deny-by-default for uncovered tool.call / agent.delegate (spec §9.3) ─────────────────
    [Fact]
    public async Task ToolCall_without_a_lease_namespace_is_denied_by_default()
    {
        var (_, t) = StartServer(s => s.RegisterAgent("tooler", async (ctx, ct) =>
        {
            await ctx.ToolCallAsync("fs.write", "c1", new { path = "/x" }, ct);
            return "done";
        }));
        await using var c = await ArcpClient.ConnectAsync(t, new ArcpClientOptions { Client = new ClientInfo { Name = "t", Version = "1" } });
        var handle = await c.SubmitAsync("tooler");
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));
        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.PermissionDenied);
    }

    [Fact]
    public async Task ToolCall_is_allowed_when_PermissiveUnleasedOperations_is_enabled()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            PermissiveUnleasedOperations = true,
        });
        server.RegisterAgent("tooler", async (ctx, ct) =>
        {
            await ctx.ToolCallAsync("fs.write", "c1", new { path = "/x" }, ct);
            return "done";
        });
        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        await using var c = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions { Client = new ClientInfo { Name = "t", Version = "1" } });
        var handle = await c.SubmitAsync("tooler");
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));
        result.Success.Should().BeTrue();
    }

    // ── #67: keyset pagination is stable and bounded, even with identical CreatedAt ──────────────
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public async Task ListJobs_pages_stably_through_jobs_that_share_a_CreatedAt()
    {
        var fixedTime = new FixedTimeProvider(DateTimeOffset.Parse("2026-06-11T00:00:00Z"));
        var (_, t) = StartServer(
            s => s.RegisterAgent("sleeper", async (ctx, ct) => { await Task.Delay(4000, ct); return null; }),
            time: fixedTime);
        await using var c = await ArcpClient.ConnectAsync(t, new ArcpClientOptions { Client = new ClientInfo { Name = "t", Version = "1" } });

        const int total = 5;
        for (var i = 0; i < total; i++) await c.SubmitAsync("sleeper");

        // Page through with a small limit; collect every job id exactly once.
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await c.ListJobsAsync(limit: 2, cursor: cursor);
            page.Jobs.Count.Should().BeLessThanOrEqualTo(2, "limit must bound page size");
            seen.AddRange(page.Jobs.Select(j => j.JobId));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        seen.Should().HaveCount(total);
        seen.Distinct().Should().HaveCount(total, "pagination must not duplicate or drop jobs across pages");
    }
}
