// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Caps;
using Arcp.Core.Ids;
using Arcp.Core.Transport;
using Arcp.Runtime.Agents;
using Arcp.Runtime.Authorization;
using Arcp.Runtime.Credentials;
using Arcp.Runtime.Leases;
using Arcp.Runtime.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arcp.Runtime;

/// <summary>Entry point for accepting ARCP sessions on the server side (spec §6.1 onwards).</summary>
public sealed class ArcpServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<SessionId, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, ResumeRegistration> _resumeTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SessionId, string> _resumeTokenBySession = new();
    private readonly ConcurrentDictionary<SessionId, SessionState> _resumableSessions = new();
    private readonly ILoggerFactory _loggerFactory;

    private readonly record struct ResumeRegistration(SessionId SessionId, DateTimeOffset ExpiresAt);

    /// <summary>Gets the options.</summary>
    public ArcpServerOptions Options { get; }

    /// <summary>Gets the agent registry.</summary>
    public AgentRegistry AgentRegistry { get; }

    /// <summary>Gets the lease manager.</summary>
    public LeaseManager LeaseManager { get; }

    /// <summary>Gets the job manager.</summary>
    public JobManager JobManager { get; }

    /// <summary>Gets the subscriptions.</summary>
    public SubscriptionManager Subscriptions { get; } = new();

    /// <summary>Gets the credential manager.</summary>
    public CredentialManager? CredentialManager { get; }

    internal IReadOnlyList<string> AdvertisedFeatures { get; }

    /// <summary>Initializes a new instance of the <see cref="ArcpServer"/> class.</summary>
    public ArcpServer(ArcpServerOptions options, ILoggerFactory? loggerFactory = null)
    {
        Options = options;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        AdvertisedFeatures = ComputeAdvertisedFeatures(options);
        CredentialManager = options.CredentialProvisioner is null
            ? null
            : new CredentialManager(
                options.CredentialProvisioner,
                options.CredentialStore,
                _loggerFactory.CreateLogger("Arcp.Credentials"),
                options.TimeProvider);
        AgentRegistry = new AgentRegistry();
        LeaseManager = new LeaseManager(options.TimeProvider);
        JobManager = new JobManager(
            AgentRegistry,
            LeaseManager,
            options.TimeProvider,
            _loggerFactory,
            CredentialManager,
            options.IdempotencyWindowSec,
            options.FatalBudgetExhaustion);
        if (CredentialManager is not null)
        {
            _ = Task.Run(() => RevokeOutstandingCredentialsAsync(CancellationToken.None));
        }
    }

    /// <summary>Register agent.</summary>
    public void RegisterAgent(string name, IAgent agent) => AgentRegistry.Register(name, agent);

    /// <summary>Register agent.</summary>
    public void RegisterAgent(string name, Func<JobContext, CancellationToken, Task<object?>> impl) =>
        AgentRegistry.Register(name, new DelegateAgent(impl));

    /// <summary>Register agent version.</summary>
    public void RegisterAgentVersion(string name, string version, IAgent agent) =>
        AgentRegistry.RegisterVersion(name, version, agent);

    /// <summary>Register agent version.</summary>
    public void RegisterAgentVersion(string name, string version, Func<JobContext, CancellationToken, Task<object?>> impl) =>
        AgentRegistry.RegisterVersion(name, version, new DelegateAgent(impl));

    /// <summary>Set default agent version.</summary>
    public void SetDefaultAgentVersion(string name, string version) => AgentRegistry.SetDefaultVersion(name, version);

    /// <summary>Accept (asynchronous).</summary>
    public async Task AcceptAsync(ITransport transport, CancellationToken cancellationToken = default)
    {
        var session = new SessionState(transport, this, Options,
            _loggerFactory.CreateLogger("Arcp.Session"), cancellationToken);
        var initialId = session.SessionId;
        _sessions[initialId] = session;
        try
        {
            await session.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessions.TryRemove(initialId, out _);
            _sessions.TryRemove(session.SessionId, out _);
            Subscriptions.RemoveSession(session.SessionId);
        }
    }

    /// <summary>Called when a freshly accepted session adopts a previously-issued session id via
    /// a valid resume token. Reroutes the live-sessions index and clears the dormant placeholder.</summary>
    internal void OnSessionAdoptedResumedId(SessionId previousLiveId, SessionState session)
    {
        if (previousLiveId.Value != session.SessionId.Value)
        {
            _sessions.TryRemove(previousLiveId, out _);
        }
        _sessions[session.SessionId] = session;
        _resumableSessions.TryRemove(session.SessionId, out _);
    }

    internal SessionState? GetSession(SessionId id) => _sessions.TryGetValue(id, out var s) ? s : null;

    internal void RemoveSession(SessionState session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        Subscriptions.RemoveSession(session.SessionId);
        // Keep the session shell available for resume until its window expires (spec §6.3).
        _resumableSessions[session.SessionId] = session;
    }

    /// <summary>Register or rotate the resume token for a session (spec §6.3). Replaces any prior token
    /// for the same session.</summary>
    internal void RegisterResumeToken(SessionState session, string newToken)
    {
        if (_resumeTokenBySession.TryGetValue(session.SessionId, out var prior) &&
            !string.Equals(prior, newToken, StringComparison.Ordinal))
        {
            _resumeTokens.TryRemove(prior, out _);
        }
        var expiresAt = Options.TimeProvider.GetUtcNow()
            .AddSeconds(Math.Max(1, Options.ResumeWindowSec));
        _resumeTokens[newToken] = new ResumeRegistration(session.SessionId, expiresAt);
        _resumeTokenBySession[session.SessionId] = newToken;
    }

    internal bool TryResume(string resumeToken, out SessionState? session)
    {
        session = null;
        if (!_resumeTokens.TryGetValue(resumeToken, out var reg)) return false;

        if (reg.ExpiresAt <= Options.TimeProvider.GetUtcNow())
        {
            _resumeTokens.TryRemove(resumeToken, out _);
            _resumeTokenBySession.TryRemove(reg.SessionId, out _);
            _resumableSessions.TryRemove(reg.SessionId, out _);
            return false;
        }

        if (_sessions.TryGetValue(reg.SessionId, out var live))
        {
            session = live;
            return true;
        }
        if (_resumableSessions.TryGetValue(reg.SessionId, out var dormant))
        {
            session = dormant;
            return true;
        }
        return false;
    }

    /// <summary>Dispose (asynchronous).</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            try { await session.CloseAsync().ConfigureAwait(false); } catch { /* shutdown */ }
        }
        _sessions.Clear();
    }

    private static IReadOnlyList<string> ComputeAdvertisedFeatures(ArcpServerOptions options)
    {
        var requested = options.Features ?? FeatureSet.AllFeatures;
        if (options.CredentialProvisioner is not null) return requested;

        if (FeatureSet.Has(options.Features, FeatureFlags.ProvisionedCredentials))
        {
            throw new ArgumentException(
                "provisioned_credentials advertised without an ICredentialProvisioner (spec §14 credential revocation reliability)",
                nameof(options));
        }

        return requested
            .Where(f => !string.Equals(f, FeatureFlags.ProvisionedCredentials, StringComparison.Ordinal) &&
                        !string.Equals(f, FeatureFlags.ModelUse, StringComparison.Ordinal))
            .ToArray();
    }

    private async Task RevokeOutstandingCredentialsAsync(CancellationToken cancellationToken)
    {
        if (CredentialManager is null) return;
        var outstanding = await Options.CredentialStore.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var jobId in outstanding.Keys)
        {
            await CredentialManager.RevokeAllForJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        }
    }
}
