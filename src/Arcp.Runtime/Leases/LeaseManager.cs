// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using Arcp.Core.Errors;
using Arcp.Core.Leases;

namespace Arcp.Runtime.Leases;

/// <summary>Lease enforcement and subsetting (spec §9).</summary>
public sealed class LeaseManager
{
    private readonly TimeProvider _time;

    public LeaseManager(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Throws if the requested lease violates v1.1 invariants. Returns the effective lease.</summary>
    public Lease Authorize(Lease? requested, LeaseConstraints? constraints)
    {
        var effective = requested ?? new Lease();

        if (constraints?.ExpiresAt is { } expires)
        {
            if (expires.Offset != TimeSpan.Zero)
                throw new InvalidRequestException("lease_constraints.expires_at MUST be UTC ('Z' suffix). (spec §9.5)");
            if (expires <= _time.GetUtcNow())
                throw new InvalidRequestException("lease_constraints.expires_at MUST be in the future. (spec §9.5)");
        }

        if (effective.Capabilities.TryGetValue(LeaseNamespaces.CostBudget, out var budgetList))
        {
            foreach (var amt in budgetList)
            {
                if (!BudgetAmount.TryParse(amt, out _))
                    throw new InvalidRequestException($"Malformed cost.budget amount: '{amt}' (spec §9.6).");
            }
        }

        if (effective.Capabilities.TryGetValue(LeaseNamespaces.ModelUse, out var modelPatterns))
        {
            foreach (var pattern in modelPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    throw new InvalidRequestException("model.use lease patterns MUST be non-empty strings (spec §9.7).");
            }
        }

        return effective;
    }

    /// <summary>Validate that the child lease is a subset of the parent (spec §9.4).</summary>
    public void AssertSubset(Lease parent, Lease child, IReadOnlyDictionary<string, decimal>? parentBudgetRemaining = null,
                              LeaseConstraints? parentConstraints = null, LeaseConstraints? childConstraints = null)
    {
        // Capability namespace subset check: each child entry MUST be matched by a parent pattern.
        foreach (var kv in child.Capabilities)
        {
            if (!parent.Capabilities.TryGetValue(kv.Key, out var parentPatterns))
                throw new LeaseSubsetViolationException($"Child lease introduces namespace '{kv.Key}' absent on parent");

            if (kv.Key == LeaseNamespaces.CostBudget)
            {
                CheckBudgetSubset(kv.Value, parentPatterns, parentBudgetRemaining);
                continue;
            }

            if (kv.Key == LeaseNamespaces.ModelUse)
            {
                CheckPatternSubset(kv.Key, kv.Value, parentPatterns);
                continue;
            }

            CheckPatternSubset(kv.Key, kv.Value, parentPatterns);
        }

        // Lease expiration subsetting (spec §9.4 addendum).
        if (parentConstraints?.ExpiresAt is { } parentExp)
        {
            if (childConstraints?.ExpiresAt is { } childExp)
            {
                if (childExp > parentExp)
                    throw new LeaseSubsetViolationException("Child lease_constraints.expires_at MUST NOT exceed parent's (spec §9.4)");
            }
            // No child constraints → child implicitly inherits parent expiry. (spec §9.4)
        }
    }

    private static void CheckPatternSubset(string namespaceName, IReadOnlyList<string> childPatterns,
        IReadOnlyList<string> parentPatterns)
    {
        foreach (var pat in childPatterns)
        {
            if (string.IsNullOrWhiteSpace(pat))
                throw new InvalidRequestException($"Child pattern for '{namespaceName}' MUST be non-empty.");
            if (!parentPatterns.Any(pp => GlobMatch(pat, pp)))
                throw new LeaseSubsetViolationException($"Child pattern '{pat}' is not within parent for '{namespaceName}'");
        }
    }

    private static void CheckBudgetSubset(IReadOnlyList<string> childPatterns, IReadOnlyList<string> parentPatterns,
                                          IReadOnlyDictionary<string, decimal>? parentRemaining)
    {
        var parentByCurrency = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var p in parentPatterns)
        {
            if (BudgetAmount.TryParse(p, out var pa))
                parentByCurrency[pa.Currency] = pa.Amount;
        }

        foreach (var cp in childPatterns)
        {
            if (!BudgetAmount.TryParse(cp, out var c))
                throw new InvalidRequestException($"Malformed child cost.budget amount '{cp}'");
            if (!parentByCurrency.TryGetValue(c.Currency, out var parentAmt))
                throw new LeaseSubsetViolationException($"Child cost.budget currency '{c.Currency}' absent on parent");
            var remaining = parentRemaining is not null && parentRemaining.TryGetValue(c.Currency, out var r) ? r : parentAmt;
            if (c.Amount > remaining)
                throw new LeaseSubsetViolationException($"Child cost.budget {c} exceeds parent's remaining {c.Currency}:{remaining}");
        }
    }

    /// <summary>Authorize a single operation against the lease. Throws <see cref="PermissionDeniedException"/>
    /// if no parent pattern matches; throws <see cref="LeaseExpiredException"/> if the lease has
    /// expired (spec §9.3, §9.5).</summary>
    public void AuthorizeOperation(Lease lease, LeaseConstraints? constraints, string namespaceName, string pattern)
    {
        if (constraints?.ExpiresAt is { } exp && _time.GetUtcNow() >= exp)
            throw new LeaseExpiredException($"Lease expired at {exp:O}");

        if (!lease.Capabilities.TryGetValue(namespaceName, out var allowed) || allowed.Count == 0)
            throw new PermissionDeniedException($"No lease for namespace '{namespaceName}'");
        foreach (var pat in allowed)
        {
            if (GlobMatch(pattern, pat)) return;
        }
        throw new PermissionDeniedException($"'{pattern}' is not authorized by lease for '{namespaceName}'");
    }

    /// <summary>Authorize a model identifier against the <c>model.use</c> lease namespace.</summary>
    public void AuthorizeModelUse(Lease lease, LeaseConstraints? constraints, string modelId) =>
        AuthorizeOperation(lease, constraints, LeaseNamespaces.ModelUse, modelId);

    internal static bool GlobMatch(string input, string pattern)
    {
        // Simple glob: '*' matches any non-empty sequence of any chars; '**' matches everything.
        if (pattern == "**" || pattern == "*") return true;
        if (string.Equals(input, pattern, StringComparison.Ordinal)) return true;

        // Convert to regex-ish prefix/suffix.
        if (pattern.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = pattern[..^3];
            return input.StartsWith(prefix, StringComparison.Ordinal);
        }
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix, StringComparison.Ordinal);
        }
        if (pattern.StartsWith("*", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return input.EndsWith(suffix, StringComparison.Ordinal);
        }
        return false;
    }
}
