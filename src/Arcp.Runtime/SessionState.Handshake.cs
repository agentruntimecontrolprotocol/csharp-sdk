// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;

namespace Arcp.Runtime;

public sealed partial class SessionState
{
    private async Task HandleHelloAsync(Envelope env, CancellationToken cancellationToken)
    {
        if (env.Payload is not SessionHelloPayload hello)
            throw new InvalidRequestException("session.hello payload missing");

        await VerifyAuthAsync(hello, cancellationToken).ConfigureAwait(false);
        TryResumeSession(hello);
        NegotiateFeatures(hello);
        ResumeToken = MintResumeToken();

        await SendAsync(BuildWelcome(), cancellationToken).ConfigureAwait(false);
        await ReplayBufferedEventsAsync(hello, cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyAuthAsync(SessionHelloPayload hello, CancellationToken cancellationToken)
    {
        if (_options.Auth is { } verifier)
        {
            Principal = await verifier.VerifyAsync(hello.Auth?.Token, cancellationToken).ConfigureAwait(false);
            if (Principal is null)
                throw new UnauthenticatedException("Invalid or missing bearer token");
        }
        else
        {
            Principal = new AuthPrincipal(hello.Auth?.Token ?? "anonymous");
        }
    }

    private void TryResumeSession(SessionHelloPayload hello)
    {
        // spec §6.3: if resume_token matches, keep the session_id.
        if (!string.IsNullOrEmpty(hello.ResumeToken) && _server.TryResume(hello.ResumeToken, out var resumed))
        {
            SessionId = resumed!.SessionId;
        }
    }

    private void NegotiateFeatures(SessionHelloPayload hello)
    {
        EffectiveFeatures = FeatureSet.Intersect(hello.Capabilities.Features, _options.Features);
        _heartbeatNegotiated = FeatureSet.Has(EffectiveFeatures, FeatureFlags.Heartbeat);
        _ackNegotiated = FeatureSet.Has(EffectiveFeatures, FeatureFlags.Ack);
    }

    private Envelope BuildWelcome() => new()
    {
        Type = MessageTypeNames.SessionWelcome,
        SessionId = SessionId.Value,
        Payload = new SessionWelcomePayload
        {
            Runtime = _options.Runtime,
            ResumeToken = ResumeToken,
            ResumeWindowSec = _options.ResumeWindowSec,
            HeartbeatIntervalSec = _heartbeatNegotiated ? _options.HeartbeatIntervalSec : null,
            Capabilities = new Capabilities
            {
                Encodings = _options.Encodings ?? Array.Empty<string>(),
                Features = _options.Features,
                Agents = _server.AgentRegistry.ToInventory(),
            },
        },
    };

    private async Task ReplayBufferedEventsAsync(SessionHelloPayload hello, CancellationToken cancellationToken)
    {
        if (hello.LastEventSeq is not { } last) return;
        foreach (var bufferedEnv in EventLog.ReadFrom(last))
        {
            await SendAsync(bufferedEnv, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleListJobsAsync(Envelope env, CancellationToken cancellationToken)
    {
        if (!FeatureSet.Has(EffectiveFeatures, FeatureFlags.ListJobs))
            throw new InvalidRequestException("'list_jobs' feature not negotiated (spec §6.2)");
        var payload = env.Payload as SessionListJobsPayload;
        var page = _server.JobManager.List(Principal?.Subject, _options.AuthorizationPolicy,
            payload?.Filter, payload?.Limit, payload?.Cursor, out var nextCursor);
        await SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionJobs,
            SessionId = SessionId.Value,
            Payload = new SessionJobsPayload
            {
                RequestId = env.Id,
                Jobs = page,
                NextCursor = nextCursor,
            },
        }, cancellationToken).ConfigureAwait(false);
    }
}
