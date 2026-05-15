// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Arcp.Core.Agents;
using Arcp.Core.Caps;
using Arcp.Core.Errors;

namespace Arcp.Runtime.Agents;

/// <summary>Registry of agents indexed by <c>name</c> and optional <c>version</c> (spec §7.5).</summary>
public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentEntry> _byName = new(StringComparer.Ordinal);

    public void Register(string name, IAgent agent)
    {
        if (!AgentRef.IsValidName(name)) throw new ArgumentException($"Invalid agent name '{name}'", nameof(name));
        var entry = _byName.GetOrAdd(name, _ => new AgentEntry(name));
        entry.SetUnversioned(agent);
    }

    public void RegisterVersion(string name, string version, IAgent agent)
    {
        if (!AgentRef.IsValidName(name)) throw new ArgumentException($"Invalid agent name '{name}'", nameof(name));
        if (!AgentRef.IsValidVersion(version)) throw new ArgumentException($"Invalid version '{version}'", nameof(version));
        var entry = _byName.GetOrAdd(name, _ => new AgentEntry(name));
        entry.AddVersion(version, agent);
    }

    public void SetDefaultVersion(string name, string version)
    {
        if (!_byName.TryGetValue(name, out var entry))
            throw new AgentNotAvailableException($"Agent '{name}' is not registered");
        entry.SetDefault(version);
    }

    public (AgentRef Resolved, IAgent Agent) Resolve(AgentRef requested)
    {
        if (!_byName.TryGetValue(requested.Name, out var entry))
            throw new AgentNotAvailableException($"Agent '{requested.Name}' is not registered");

        if (requested.Version is { } v)
        {
            if (!entry.TryGetVersion(v, out var versioned))
                throw new AgentVersionNotAvailableException($"{requested.Name}@{v} not registered");
            return (new AgentRef(requested.Name, v), versioned!);
        }

        if (entry.DefaultVersion is { } d && entry.TryGetVersion(d, out var def))
            return (new AgentRef(requested.Name, d), def!);
        if (entry.UnversionedAgent is { } u)
            return (new AgentRef(requested.Name), u);
        if (entry.AnyVersion is { } any)
            return (new AgentRef(requested.Name, any.Version), any.Agent);
        throw new AgentNotAvailableException($"Agent '{requested.Name}' has no registered implementation");
    }

    public IReadOnlyList<AgentInventoryEntry> ToInventory() =>
        _byName.Values.Select(e => new AgentInventoryEntry
        {
            Name = e.Name,
            Versions = e.Versions.Count > 0 ? e.Versions.Keys.ToArray() : null,
            Default = e.DefaultVersion,
        }).ToArray();

    private sealed class AgentEntry
    {
        private readonly object _gate = new();

        public string Name { get; }

        public Dictionary<string, IAgent> Versions { get; } = new(StringComparer.Ordinal);

        public string? DefaultVersion { get; private set; }

        public IAgent? UnversionedAgent { get; private set; }

        public AgentEntry(string name) { Name = name; }

        public bool TryGetVersion(string v, out IAgent? agent)
        {
            lock (_gate) return Versions.TryGetValue(v, out agent);
        }

        public void AddVersion(string version, IAgent agent)
        {
            lock (_gate)
            {
                Versions[version] = agent;
                DefaultVersion ??= version;
            }
        }

        public void SetUnversioned(IAgent agent)
        {
            lock (_gate) UnversionedAgent = agent;
        }

        public void SetDefault(string version)
        {
            lock (_gate)
            {
                if (!Versions.ContainsKey(version))
                    throw new AgentVersionNotAvailableException($"{Name}@{version} not registered");
                DefaultVersion = version;
            }
        }

        public (string Version, IAgent Agent)? AnyVersion
        {
            get
            {
                lock (_gate)
                {
                    foreach (var kv in Versions) return (kv.Key, kv.Value);
                    return null;
                }
            }
        }
    }
}
