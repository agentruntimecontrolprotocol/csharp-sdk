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

    /// <summary>Register.</summary>
    public void Register(string name, IAgent agent)
    {
        if (!AgentRef.IsValidName(name)) throw new ArgumentException($"Invalid agent name '{name}'", nameof(name));
        var entry = _byName.GetOrAdd(name, _ => new AgentEntry(name));
        entry.SetUnversioned(agent);
    }

    /// <summary>Register version.</summary>
    public void RegisterVersion(string name, string version, IAgent agent)
    {
        if (!AgentRef.IsValidName(name)) throw new ArgumentException($"Invalid agent name '{name}'", nameof(name));
        if (!AgentRef.IsValidVersion(version)) throw new ArgumentException($"Invalid version '{version}'", nameof(version));
        var entry = _byName.GetOrAdd(name, _ => new AgentEntry(name));
        entry.AddVersion(version, agent);
    }

    /// <summary>Set default version.</summary>
    public void SetDefaultVersion(string name, string version)
    {
        if (!_byName.TryGetValue(name, out var entry))
            throw new AgentNotAvailableException($"Agent '{name}' is not registered");
        entry.SetDefault(version);
    }

    /// <summary>Resolve.</summary>
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

    /// <summary>To inventory.</summary>
    public IReadOnlyList<AgentInventoryEntry> ToInventory() =>
        _byName.Values.Select(e =>
        {
            var (versions, defaultVersion) = e.SnapshotInventoryFields();
            return new AgentInventoryEntry
            {
                Name = e.Name,
                Versions = versions,
                Default = defaultVersion,
            };
        }).ToArray();

    private sealed class AgentEntry
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, IAgent> _versions = new(StringComparer.Ordinal);

        public string Name { get; }

        public string? DefaultVersion { get; private set; }

        public IAgent? UnversionedAgent { get; private set; }

        public AgentEntry(string name) { Name = name; }

        public bool TryGetVersion(string v, out IAgent? agent)
        {
            lock (_gate) return _versions.TryGetValue(v, out agent);
        }

        public void AddVersion(string version, IAgent agent)
        {
            lock (_gate)
            {
                _versions[version] = agent;
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
                if (!_versions.ContainsKey(version))
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
                    foreach (var kv in _versions) return (kv.Key, kv.Value);
                    return null;
                }
            }
        }

        /// <summary>Atomically snapshot the inventory-visible fields under the same lock that guards
        /// mutation, so <see cref="ToInventory"/> never enumerates the mutable dictionary while a
        /// concurrent registration is writing to it.</summary>
        public (IReadOnlyList<string>? Versions, string? Default) SnapshotInventoryFields()
        {
            lock (_gate)
            {
                var versions = _versions.Count > 0 ? _versions.Keys.ToArray() : null;
                return (versions, DefaultVersion);
            }
        }
    }
}
