// SPDX-License-Identifier: Apache-2.0
// samples/ProvisionedCredential: runtime-issued bearer credentials scoped by model.use,
// cost.budget, and lease_constraints.expires_at. Spec §9.7, §9.8.
using Arcp.Client;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using Arcp.Runtime.Credentials;

var provisioner = new InMemoryCredentialProvisioner();
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "provisioned-credential", Version = "1.0.0" },
    CredentialProvisioner = provisioner,
});

server.RegisterAgent("model-proxy", async (ctx, ct) =>
{
    var credential = ctx.Credentials[0];
    await ctx.StatusAsync("credential_ready", $"using {credential.Id}", ct);
    await ctx.MetricAsync("cost.model", 0.25, "USD", cancellationToken: ct);
    return new { credential = credential.Id, endpoint = credential.Endpoint };
});

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "credential-client", Version = "1.0.0" },
});

var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.ModelUse] = new[] { "tier-fast/*" },
    [LeaseNamespaces.CostBudget] = new[] { "USD:1.00" },
});
var constraints = new LeaseConstraints
{
    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
};
var handle = await client.SubmitAsync("model-proxy", leaseRequest: lease, leaseConstraints: constraints);
var credentialId = (await handle.Accepted).Credentials![0].Id;
Console.WriteLine($"issued credential: {credentialId}");
var result = await handle.Result;
Console.WriteLine($"final: {result.FinalStatus}");
for (var i = 0; i < 20 && !provisioner.RevokedIds.Contains(credentialId); i++)
{
    await Task.Delay(25);
}
Console.WriteLine($"revoked: {provisioner.RevokedIds.Contains(credentialId)}");
return 0;
