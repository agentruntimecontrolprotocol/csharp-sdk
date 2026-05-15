// SPDX-License-Identifier: Apache-2.0
// samples/AspNetCore: host an ARCP runtime on Kestrel via Arcp.AspNetCore + MapArcp("/arcp").
// Spec §4.1.
using Arcp.AspNetCore;
using Arcp.Core.Messages;
using Arcp.Runtime;

var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "aspnetcore-sample", Version = "1.0.0" },
});
server.RegisterAgent("echo", (ctx, ct) => Task.FromResult<object?>(ctx.Input));

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(server);
var app = builder.Build();
app.UseWebSockets();
app.MapArcp(server, o => o.Path = "/arcp");
app.MapGet("/healthz", () => Results.Ok("ok"));

Console.WriteLine("ARCP runtime listening on /arcp");
app.Run("http://127.0.0.1:5519");
