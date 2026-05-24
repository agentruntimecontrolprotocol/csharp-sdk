// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Transport;
using Arcp.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arcp.AspNetCore;

/// <summary>Options for <see cref="ArcpEndpointRouteBuilderExtensions.MapArcp"/>.</summary>
public sealed class ArcpEndpointOptions
{
    /// <summary>Request path the ARCP WebSocket endpoint is mounted at. Defaults to <c>/arcp</c>.</summary>
    public string Path { get; set; } = "/arcp";

    /// <summary>Optional allow-list of <c>Host</c> headers. When set, requests with other host
    /// headers are rejected with 400 before any session work happens.</summary>
    public IReadOnlyList<string>? AllowedHosts { get; set; }
}

/// <summary>Extensions for mounting the ARCP WebSocket endpoint on an ASP.NET Core app.</summary>
public static class ArcpEndpointRouteBuilderExtensions
{
    /// <summary>Map an ARCP WebSocket endpoint at <c>/arcp</c> on the given route builder.</summary>
    public static IEndpointConventionBuilder MapArcp(
        this IEndpointRouteBuilder endpoints, ArcpServer runtime, Action<ArcpEndpointOptions>? configure = null)
    {
        var options = new ArcpEndpointOptions();
        configure?.Invoke(options);

        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Arcp.AspNetCore");

        return endpoints.MapGet(options.Path, async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("ARCP endpoint requires a WebSocket upgrade.").ConfigureAwait(false);
                return;
            }
            if (options.AllowedHosts is { Count: > 0 } hosts)
            {
                var host = ctx.Request.Host.Host?.ToLowerInvariant();
                if (host is null || !hosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var transport = new WebSocketTransport(socket, ownsSocket: false);
            try
            {
                await runtime.AcceptAsync(transport, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "ARCP session terminated");
            }
        });
    }
}
