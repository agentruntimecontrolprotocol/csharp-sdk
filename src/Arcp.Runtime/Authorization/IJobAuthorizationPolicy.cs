// SPDX-License-Identifier: Apache-2.0
using Arcp.Core.Auth;

namespace Arcp.Runtime.Authorization;

/// <summary>The seam that gates <c>session.list_jobs</c> and <c>job.subscribe</c> across principals
/// (spec §6.6, §7.6). Default: same-principal.</summary>
public interface IJobAuthorizationPolicy
{
    bool CanObserve(string? jobSubmitterPrincipal, AuthPrincipal? requestor);
}

/// <summary>Default policy: only the submitting principal may observe a job (spec §6.6, §7.6 default).</summary>
public sealed class SamePrincipalPolicy : IJobAuthorizationPolicy
{
    public bool CanObserve(string? jobSubmitterPrincipal, AuthPrincipal? requestor)
    {
        if (string.IsNullOrEmpty(jobSubmitterPrincipal)) return false;
        if (requestor is null) return false;
        return string.Equals(jobSubmitterPrincipal, requestor.Subject, System.StringComparison.Ordinal);
    }
}

/// <summary>Permissive policy: any authenticated principal may observe any job.</summary>
public sealed class AllowAllPolicy : IJobAuthorizationPolicy
{
    public bool CanObserve(string? jobSubmitterPrincipal, AuthPrincipal? requestor) => requestor is not null;
}
