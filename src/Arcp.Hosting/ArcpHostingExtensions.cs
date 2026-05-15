// SPDX-License-Identifier: Apache-2.0
using System;
using Arcp.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arcp.Hosting;

/// <summary>DI helpers for hosting an ARCP runtime inside a generic host.</summary>
public static class ArcpHostingExtensions
{
    /// <summary>Register an <see cref="ArcpServer"/> as a singleton plus an options binding.</summary>
    public static IServiceCollection AddArcpRuntime(this IServiceCollection services, Action<ArcpServerOptions> configure)
    {
        services.AddOptions<ArcpServerOptions>().Configure(configure);
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ArcpServerOptions>>().Value;
            var loggers = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
            return new ArcpServer(opts, loggers);
        });
        return services;
    }
}
