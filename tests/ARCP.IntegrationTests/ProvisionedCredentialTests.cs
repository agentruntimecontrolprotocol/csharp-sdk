// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Auth;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using Arcp.Runtime.Authorization;
using Arcp.Runtime.Credentials;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class ProvisionedCredentialTests
{
    [Fact]
    public async Task Job_accepted_carries_credentials_when_provisioner_configured()
    {
        var provisioner = new InMemoryCredentialProvisioner();
        var (_, transport) = StartServer(provisioner, s =>
            s.RegisterAgent("llm", (ctx, ct) => Task.FromResult<object?>(ctx.Credentials[0].Id)));
        await using var client = await ConnectAsync(transport);

        var handle = await client.SubmitAsync("llm", leaseRequest: Lease());
        var accepted = await handle.Accepted;

        accepted.Credentials.Should().ContainSingle();
        accepted.Credentials![0].Value.Should().NotBeEmpty();
        accepted.Credentials[0].Constraints!.ModelUse.Should().Equal("tier-fast/*");
        await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Credentials_revoked_on_success()
    {
        var provisioner = new InMemoryCredentialProvisioner();
        var (_, transport) = StartServer(provisioner, s =>
            s.RegisterAgent("done", (ctx, ct) => Task.FromResult<object?>("ok")));
        await using var client = await ConnectAsync(transport);

        var handle = await client.SubmitAsync("done", leaseRequest: Lease());
        var credentialId = (await handle.Accepted).Credentials![0].Id;
        await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));

        await EventuallyAsync(() => provisioner.RevokedIds.Contains(credentialId));
    }

    [Fact]
    public async Task Credentials_revoked_on_cancellation()
    {
        var provisioner = new InMemoryCredentialProvisioner();
        var (_, transport) = StartServer(provisioner, s =>
            s.RegisterAgent("wait", async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return null;
            }));
        await using var client = await ConnectAsync(transport);

        var handle = await client.SubmitAsync("wait", leaseRequest: Lease());
        var credentialId = (await handle.Accepted).Credentials![0].Id;
        await handle.CancelAsync("stop");
        await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));

        await EventuallyAsync(() => provisioner.RevokedIds.Contains(credentialId));
    }

    [Fact]
    public async Task Subscriber_other_principal_sees_null_credentials()
    {
        var provisioner = new InMemoryCredentialProvisioner();
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            CredentialProvisioner = provisioner,
            AuthorizationPolicy = new AllowAllPolicy(),
            Auth = new StaticBearerVerifier(new Dictionary<string, AuthPrincipal>
            {
                ["submitter-token"] = new("submitter"),
                ["observer-token"] = new("observer"),
            }),
        });
        server.RegisterAgent("wait", async (ctx, ct) =>
        {
            await Task.Delay(300, ct);
            return "done";
        });
        var submitterTransport = Accept(server);
        var observerTransport = Accept(server);

        await using var submitter = await ConnectAsync(submitterTransport, "submitter-token");
        await using var observer = await ConnectAsync(observerTransport, "observer-token");
        var handle = await submitter.SubmitAsync("wait", leaseRequest: Lease());

        var subscription = await observer.SubscribeAsync(handle.JobId);
        var ack = await subscription.Acknowledged;

        ack.Credentials.Should().BeNull();
        await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Rotation_emits_status_event_and_revokes_prior()
    {
        var provisioner = new InMemoryCredentialProvisioner();
        var (_, transport) = StartServer(provisioner, s =>
            s.RegisterAgent("rotate", async (ctx, ct) =>
            {
                var current = ctx.Credentials[0];
                await ctx.RotateCredentialAsync(current.Id, new ProvisionedCredential
                {
                    Id = current.Id,
                    Value = "rotated-secret",
                    Endpoint = current.Endpoint,
                    Constraints = current.Constraints,
                }, ct);
                return "rotated";
            }));
        await using var client = await ConnectAsync(transport);

        var handle = await client.SubmitAsync("rotate", leaseRequest: Lease());
        var credentialId = (await handle.Accepted).Credentials![0].Id;
        StatusBody? rotated = null;
        await foreach (var ev in handle.Events(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token))
        {
            if (ev.Kind != EventKinds.Status) continue;
            rotated = ev.BodyAs<StatusBody>();
            if (rotated?.Phase == StatusPhases.CredentialRotated) break;
        }

        rotated.Should().NotBeNull();
        rotated!.CredentialId.Should().Be(credentialId);
        rotated.CredentialValue.Should().Be("rotated-secret");
        provisioner.RevokedIds.Should().Contain(credentialId);
        await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Delegated_child_credential_is_constrained_to_child_lease()
    {
        var provisioner = new InMemoryCredentialProvisioner();
        var (_, transport) = StartServer(provisioner, s =>
        {
            s.RegisterAgent("parent", async (ctx, ct) =>
            {
                await Task.Delay(250, ct);
                return "parent";
            });
            s.RegisterAgent("child", (ctx, ct) => Task.FromResult<object?>("child"));
        });
        await using var client = await ConnectAsync(transport);
        var parentLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.ModelUse] = new[] { "tier-fast/*", "tier-premium/*" },
            [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
        });
        var parent = await client.SubmitAsync("parent", leaseRequest: parentLease);
        var childLease = new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            [LeaseNamespaces.ModelUse] = new[] { "tier-fast/*" },
            [LeaseNamespaces.CostBudget] = new[] { "USD:0.50" },
        });

        var child = await client.SubmitAsync("child", leaseRequest: childLease, parentJobId: parent.JobId.Value);
        var accepted = await child.Accepted;

        accepted.Credentials![0].Constraints!.ModelUse.Should().Equal("tier-fast/*");
        await child.Result.WaitAsync(TimeSpan.FromSeconds(5));
        await parent.Result.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static (ArcpServer Server, MemoryTransport Transport) StartServer(
        InMemoryCredentialProvisioner provisioner,
        Action<ArcpServer> configure)
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            CredentialProvisioner = provisioner,
        });
        configure(server);
        return (server, Accept(server));
    }

    private static MemoryTransport Accept(ArcpServer server)
    {
        var (client, runtime) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(runtime));
        return client;
    }

    private static Task<ArcpClient> ConnectAsync(MemoryTransport transport, string? token = null) =>
        ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "test", Version = "1.0" },
            Token = token,
        });

    private static Lease Lease() => new(new Dictionary<string, IReadOnlyList<string>>
    {
        [LeaseNamespaces.ModelUse] = new[] { "tier-fast/*" },
        [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
    });

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(25);
        }

        predicate().Should().BeTrue();
    }
}
