// SPDX-License-Identifier: Apache-2.0
// samples/CustomAuth: a custom IBearerVerifier rejects bad tokens with UNAUTHENTICATED. Spec §6.1.
using Arcp.Client;
using Arcp.Core.Auth;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "custom-auth", Version = "1.0.0" },
    Auth = new StaticBearerVerifier(("secret-1", new AuthPrincipal("alice@example.com"))),
});
server.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>("ok"));

var (clientT, serverT) = MemoryTransport.Pair();
_ = server.AcceptAsync(serverT);
await using var client = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "auth-client", Version = "1.0.0" },
    Token = "secret-1",
});
Console.WriteLine($"authenticated session={client.SessionId}");
var handle = await client.SubmitAsync("noop");
var res = await handle.Result;
Console.WriteLine($"job: {res.FinalStatus}");
return 0;
