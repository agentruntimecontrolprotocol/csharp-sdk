// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Tracing;
using Arcp.Core.Transport;
using Arcp.Core.Wire;

namespace Arcp.Otel;

/// <summary>Extension that wraps an <see cref="ITransport"/> with OpenTelemetry-flavored
/// <see cref="ActivitySource"/> instrumentation (spec §11).</summary>
public static class ArcpTracing
{
    public const string OtelExtensionName = "x-vendor.opentelemetry.tracecontext";

    public static ITransport WithTracing(this ITransport inner) => new TracingTransport(inner);

    private sealed class TracingTransport : ITransport
    {
        private readonly ITransport _inner;

        public TracingTransport(ITransport inner) { _inner = inner; }

        public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            using var activity = ArcpDiagnostics.Transport.StartActivity($"arcp.send {envelope.Type}", ActivityKind.Producer);
            SetCommonAttributes(activity, envelope, direction: "out");
            // Inject W3C traceparent into envelope.extensions.
            Envelope toSend = envelope;
            if (Activity.Current is { } current && current.IdFormat == ActivityIdFormat.W3C)
            {
                var ctxObj = new Dictionary<string, JsonElement>
                {
                    ["traceparent"] = JsonSerializer.SerializeToElement(current.Id ?? string.Empty),
                };
                if (!string.IsNullOrEmpty(current.TraceStateString))
                {
                    ctxObj["tracestate"] = JsonSerializer.SerializeToElement(current.TraceStateString);
                }
                var ext = envelope.Extensions is null
                    ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    : new Dictionary<string, JsonElement>(envelope.Extensions, StringComparer.Ordinal);
                ext[OtelExtensionName] = JsonSerializer.SerializeToElement(ctxObj);
                toSend = envelope with { Extensions = ext };
            }
            await _inner.SendAsync(toSend, cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<Envelope> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var env in _inner.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                ActivityContext parent = default;
                if (env.Extensions is not null && env.Extensions.TryGetValue(OtelExtensionName, out var ctxEl))
                {
                    if (ctxEl.TryGetProperty("traceparent", out var tp))
                    {
                        var tpStr = tp.GetString();
                        string? tsStr = ctxEl.TryGetProperty("tracestate", out var ts) ? ts.GetString() : null;
                        ActivityContext.TryParse(tpStr, tsStr, out parent);
                    }
                }
                using var activity = ArcpDiagnostics.Transport.StartActivity($"arcp.recv {env.Type}", ActivityKind.Consumer, parent);
                SetCommonAttributes(activity, env, direction: "in");
                yield return env;
            }
        }

        public ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
            => _inner.CloseAsync(reason, cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private static void SetCommonAttributes(Activity? activity, Envelope env, string direction)
    {
        if (activity is null) return;
        activity.SetTag(TraceAttributes.Direction, direction);
        activity.SetTag(TraceAttributes.Type, env.Type);
        activity.SetTag(TraceAttributes.Id, env.Id);
        if (env.SessionId is not null) activity.SetTag(TraceAttributes.SessionId, env.SessionId);
        if (env.JobId is not null) activity.SetTag(TraceAttributes.JobId, env.JobId);
        if (env.TraceId is not null) activity.SetTag(TraceAttributes.TraceId, env.TraceId);
        if (env.EventSeq is { } seq) activity.SetTag(TraceAttributes.EventSeq, seq);
    }
}
