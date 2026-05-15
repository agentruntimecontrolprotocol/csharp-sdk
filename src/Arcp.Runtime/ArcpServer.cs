// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;
using Arcp.Core.Transport;
using Arcp.Runtime.Agents;
using Arcp.Runtime.Authorization;
using Arcp.Runtime.Leases;
using Arcp.Runtime.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arcp.Runtime;

/// <summary>Entry point for accepting ARCP sessions on the server side (spec §6.1 onwards).</summary>
public sealed class ArcpServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<SessionId, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, SessionId> _resumeTokens = new(StringComparer.Ordinal);
    private readonly ILoggerFactory _loggerFactory;

    public ArcpServerOptions Options { get; }

    public AgentRegistry AgentRegistry { get; }

    public LeaseManager LeaseManager { get; }

    public JobManager JobManager { get; }

    public SubscriptionManager Subscriptions { get; } = new();

    public ArcpServer(ArcpServerOptions options, ILoggerFactory? loggerFactory = null)
    {
        Options = options;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        AgentRegistry = new AgentRegistry();
        LeaseManager = new LeaseManager(options.TimeProvider);
        JobManager = new JobManager(AgentRegistry, LeaseManager, options.TimeProvider, _loggerFactory);
    }

    public void RegisterAgent(string name, IAgent agent) => AgentRegistry.Register(name, agent);

    public void RegisterAgent(string name, Func<JobContext, CancellationToken, Task<object?>> impl) =>
        AgentRegistry.Register(name, new DelegateAgent(impl));

    public void RegisterAgentVersion(string name, string version, IAgent agent) =>
        AgentRegistry.RegisterVersion(name, version, agent);

    public void RegisterAgentVersion(string name, string version, Func<JobContext, CancellationToken, Task<object?>> impl) =>
        AgentRegistry.RegisterVersion(name, version, new DelegateAgent(impl));

    public void SetDefaultAgentVersion(string name, string version) => AgentRegistry.SetDefaultVersion(name, version);

    public async Task AcceptAsync(ITransport transport, CancellationToken cancellationToken = default)
    {
        var session = new SessionState(transport, this, Options,
            _loggerFactory.CreateLogger("Arcp.Session"), cancellationToken);
        _sessions[session.SessionId] = session;
        try
        {
            await session.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessions.TryRemove(session.SessionId, out _);
            Subscriptions.RemoveSession(session.SessionId);
        }
    }

    internal SessionState? GetSession(SessionId id) => _sessions.TryGetValue(id, out var s) ? s : null;

    internal void RemoveSession(SessionState session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        Subscriptions.RemoveSession(session.SessionId);
    }

    internal bool TryResume(string resumeToken, out SessionState? session)
    {
        if (_resumeTokens.TryGetValue(resumeToken, out var id) && _sessions.TryGetValue(id, out var s))
        {
            session = s;
            return true;
        }
        session = null;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            try { await session.CloseAsync().ConfigureAwait(false); } catch { /* shutdown */ }
        }
        _sessions.Clear();
    }
}
