// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.AspNetCore;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arcp.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0] switch
        {
            "serve" => await Serve(args).ConfigureAwait(false),
            "submit" => await Submit(args).ConfigureAwait(false),
            "version" => Version(),
            "--help" or "-h" => PrintUsage(),
            _ => Unknown(args[0]),
        };
    }

    private static int PrintUsage()
    {
        Console.WriteLine("ARCP " + ArcpInfo.ProtocolVersion + " CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  arcp serve  --host 127.0.0.1 --port 7777 --token TOK");
        Console.WriteLine("  arcp submit --url ws://127.0.0.1:7777/arcp --token TOK --agent echo --input '{\"hi\":1}'");
        Console.WriteLine("  arcp version");
        return 0;
    }

    private static int Version()
    {
        Console.WriteLine("arcp " + ArcpInfo.ProtocolVersion);
        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"unknown command: {cmd}");
        return 2;
    }

    private static async Task<int> Serve(string[] args)
    {
        var host = ParseFlag(args, "--host") ?? "127.0.0.1";
        var port = int.Parse(ParseFlag(args, "--port") ?? "7777");
        var token = ParseFlag(args, "--token") ?? "tok-demo";

        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "arcp-cli", Version = "1.0.0" },
            Auth = new Arcp.Core.Auth.StaticBearerVerifier((token, new Arcp.Core.Auth.AuthPrincipal("cli"))),
        });
        server.RegisterAgent("echo", async (ctx, ct) =>
        {
            await ctx.LogAsync("info", "echo received", ct).ConfigureAwait(false);
            return ctx.Input;
        });

        var builder = WebApplication.CreateBuilder();
        builder.Logging.AddConsole();
        builder.WebHost.UseUrls($"http://{host}:{port}");
        builder.Services.AddSingleton(server);
        var app = builder.Build();
        app.UseWebSockets();
        app.MapArcp(server);
        app.MapGet("/healthz", () => Results.Ok("ok"));
        Console.WriteLine($"ARCP runtime listening on ws://{host}:{port}/arcp");
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> Submit(string[] args)
    {
        var url = ParseFlag(args, "--url") ?? "ws://127.0.0.1:7777/arcp";
        var token = ParseFlag(args, "--token") ?? "tok-demo";
        var agent = ParseFlag(args, "--agent") ?? "echo";
        var input = ParseFlag(args, "--input") ?? "{}";

        var ws = new System.Net.WebSockets.ClientWebSocket();
        await ws.ConnectAsync(new Uri(url), CancellationToken.None).ConfigureAwait(false);
        var transport = new WebSocketTransport(ws, ownsSocket: true);
        await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "arcp-cli", Version = "1.0.0" },
            Token = token,
        }, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"connected: session_id={client.SessionId} features={string.Join(',', client.EffectiveFeatures)}");

        var handle = await client.SubmitAsync(agent, System.Text.Json.JsonDocument.Parse(input).RootElement.Clone()).ConfigureAwait(false);
        Console.WriteLine($"accepted: job_id={handle.JobId}");
        var res = await handle.Result.ConfigureAwait(false);
        Console.WriteLine($"result: status={res.FinalStatus}");
        return res.Success ? 0 : 1;
    }

    private static string? ParseFlag(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
