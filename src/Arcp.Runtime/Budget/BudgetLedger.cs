// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Arcp.Core.Errors;
using Arcp.Core.Leases;

namespace Arcp.Runtime.Budget;

/// <summary>Per-currency budget counter tracker (spec §9.6).</summary>
public sealed class BudgetLedger
{
    private readonly ConcurrentDictionary<string, decimal> _remaining = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, decimal> _initial = new(StringComparer.Ordinal);

    /// <summary>Gets the is active.</summary>
    public bool IsActive => !_remaining.IsEmpty;

    /// <summary>Gets the remaining.</summary>
    public IReadOnlyDictionary<string, decimal> Remaining => _remaining.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>Gets the initial.</summary>
    public IReadOnlyDictionary<string, decimal> Initial => _initial.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>Initialize counters from a lease's <c>cost.budget</c> patterns.</summary>
    public void Initialize(Lease lease)
    {
        if (!lease.Capabilities.TryGetValue(LeaseNamespaces.CostBudget, out var patterns)) return;
        foreach (var p in patterns)
        {
            if (BudgetAmount.TryParse(p, out var amt))
            {
                _remaining[amt.Currency] = amt.Amount;
                _initial[amt.Currency] = amt.Amount;
            }
        }
    }

    /// <summary>Apply a <c>cost.*</c> metric. Negative values are rejected per spec §9.6 (silent no-op).
    /// Returns whether anything changed.</summary>
    public bool ApplyMetric(string metricName, double value, string? unit)
    {
        if (!metricName.StartsWith("cost.", StringComparison.Ordinal)) return false;
        if (metricName == "cost.budget.remaining") return false;
        if (string.IsNullOrEmpty(unit)) return false;
        if (value < 0) return false;
        if (!_remaining.ContainsKey(unit)) return false;
        var dec = (decimal)value;
        _remaining.AddOrUpdate(unit, _ => -dec, (_, prev) => prev - dec);
        return true;
    }

    /// <summary>Throws <see cref="BudgetExhaustedException"/> if any counter has hit zero.</summary>
    public void AssertNotExhausted()
    {
        foreach (var kv in _remaining)
        {
            if (kv.Value <= 0)
                throw new BudgetExhaustedException($"{kv.Key} budget exhausted (remaining={kv.Value})");
        }
    }

    /// <summary>Returns true when a specific currency has been exhausted.</summary>
    public bool IsExhausted(string currency) =>
        _remaining.TryGetValue(currency, out var r) && r <= 0;
}
