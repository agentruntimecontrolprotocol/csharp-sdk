// SPDX-License-Identifier: Apache-2.0
using System;
using System.Text.Json;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Client;

/// <summary>A typed view on a received <c>job.event</c> envelope.</summary>
public sealed record JobEvent(string Kind, DateTimeOffset Ts, JsonElement Body, long EventSeq, string? JobId)
{
    public T? BodyAs<T>() => Body.Deserialize<T>(ArcpJson.Options);

    public static JobEvent From(Envelope env)
    {
        if (env.Payload is not JobEventPayload p)
            throw new InvalidOperationException("Envelope is not a job.event");
        return new JobEvent(p.Kind, p.Ts, p.Body, env.EventSeq ?? 0, env.JobId);
    }
}
