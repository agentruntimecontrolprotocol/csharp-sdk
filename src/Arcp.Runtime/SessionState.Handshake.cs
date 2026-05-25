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
        _server.RegisterResumeToken(this, ResumeToken);

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
        // spec §6.3: if resume_token matches, keep the session_id and inherit the prior
        // session's durable state so replay can serve gap-free events.
        if (string.IsNullOrEmpty(hello.ResumeToken)) return;

        if (_server.TryResume(hello.ResumeToken, out var resumed))
        {
            var previousLiveId = SessionId;
            AdoptResumableStateFrom(resumed!);
            _server.OnSessionAdoptedResumedId(previousLiveId, this);
            return;
        }

        // Token presented but unknown or expired (spec §6.3 / §12 RESUME_WINDOW_EXPIRED).
        throw new ResumeWindowExpiredException(
            "Resume token unknown or outside ResumeWindowSec");
    }

    private void NegotiateFeatures(SessionHelloPayload hello)
    {
        EffectiveFeatures = FeatureSet.Intersect(hello.Capabilities.Features, _server.AdvertisedFeatures);
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
                Features = _server.AdvertisedFeatures,
                Agents = _server.AgentRegistry.ToInventory(),
            },
        },
    };

    private async Task ReplayBufferedEventsAsync(SessionHelloPayload hello, CancellationToken cancellationToken)
    {
        if (hello.LastEventSeq is not { } last) return;
        await ReplayFromSeqAsync(last, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replay events with <c>seq &gt; fromSeq</c>. Throws <c>RESUME_WINDOW_EXPIRED</c> if
    /// the buffer no longer covers the requested point (spec §6.3).</summary>
    private async Task ReplayFromSeqAsync(long fromSeq, CancellationToken cancellationToken)
    {
        var earliest = EventLog.EarliestSeq;
        // Buffer is non-empty and the requested point pre-dates the oldest retained event.
        // (`earliest > fromSeq + 1` means events `fromSeq+1 .. earliest-1` were evicted.)
        if (earliest > 0 && fromSeq + 1 < earliest)
        {
            throw new ResumeWindowExpiredException(
                $"Buffer no longer covers requested last_event_seq={fromSeq}; earliest buffered seq is {earliest}.");
        }

        foreach (var bufferedEnv in EventLog.ReadFrom(fromSeq))
        {
            await SendAsync(bufferedEnv, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Handle a spec §6.3 <c>session.resume</c> envelope. Adopts the prior session's
    /// durable state (event log + session_id) and replays buffered events. Unlike
    /// <c>session.hello</c>, this does NOT rotate the resume token or emit a fresh
    /// <c>session.welcome</c> — it is the protocol-level "fast reconnect" path.</summary>
    internal async Task HandleResumeAsync(Envelope env, SessionResumePayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(payload.ResumeToken))
            throw new InvalidRequestException("session.resume requires a non-empty resume_token (spec §6.3)");

        if (!_server.TryResume(payload.ResumeToken, out var resumed) || resumed is null)
            throw new ResumeWindowExpiredException("Resume token unknown or outside ResumeWindowSec");

        // Adopt the prior session's durable state (session_id + event log) so replays come
        // from the correct buffer.
        var previousLiveId = SessionId;
        AdoptResumableStateFrom(resumed);
        _server.OnSessionAdoptedResumedId(previousLiveId, this);

        // Feature set is unknown at this point because we never saw a hello — treat the resume
        // attempt as inheriting the prior session's features (already reflected in EventLog).
        if (payload.LastEventSeq is { } last)
        {
            await ReplayFromSeqAsync(last, cancellationToken).ConfigureAwait(false);
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
