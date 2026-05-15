// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Arcp.Core.Auth;

/// <summary>Authenticated principal extracted from a successful bearer-token check (spec §6.1).</summary>
public sealed record AuthPrincipal(string Subject, IReadOnlyDictionary<string, string>? Claims = null);

/// <summary>Bearer-token verification seam (spec §6.1). The runtime calls this once per session
/// on <c>session.hello</c>; bad tokens surface as <c>UNAUTHENTICATED</c>.</summary>
public interface IBearerVerifier
{
    ValueTask<AuthPrincipal?> VerifyAsync(string? token, CancellationToken cancellationToken = default);
}

/// <summary>A simple in-memory bearer verifier mapping tokens to principals.</summary>
public sealed class StaticBearerVerifier : IBearerVerifier
{
    private readonly IReadOnlyDictionary<string, AuthPrincipal> _table;

    public StaticBearerVerifier(IReadOnlyDictionary<string, AuthPrincipal> table)
    {
        _table = table;
    }

    public StaticBearerVerifier(params (string Token, AuthPrincipal Principal)[] entries)
    {
        var dict = new Dictionary<string, AuthPrincipal>(StringComparer.Ordinal);
        foreach (var (t, p) in entries) dict[t] = p;
        _table = dict;
    }

    public ValueTask<AuthPrincipal?> VerifyAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token)) return ValueTask.FromResult<AuthPrincipal?>(null);
        return ValueTask.FromResult<AuthPrincipal?>(_table.TryGetValue(token, out var p) ? p : null);
    }
}

/// <summary>A verifier that accepts any non-empty token and reflects it as the principal subject.</summary>
public sealed class AllowAnyBearerVerifier : IBearerVerifier
{
    public ValueTask<AuthPrincipal?> VerifyAsync(string? token, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AuthPrincipal?>(string.IsNullOrEmpty(token) ? null : new AuthPrincipal(token));
}
