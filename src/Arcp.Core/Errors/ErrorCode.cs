// SPDX-License-Identifier: Apache-2.0
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Arcp.Core.Errors;

/// <summary>Canonical ARCP v1.1 error codes (spec §12). Strings, not an enum, so unknown
/// deployment-specific codes round-trip on the wire.</summary>
public static class ErrorCode
{
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string LeaseSubsetViolation = "LEASE_SUBSET_VIOLATION";
    public const string JobNotFound = "JOB_NOT_FOUND";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string AgentNotAvailable = "AGENT_NOT_AVAILABLE";
    public const string AgentVersionNotAvailable = "AGENT_VERSION_NOT_AVAILABLE";
    public const string Cancelled = "CANCELLED";
    public const string Timeout = "TIMEOUT";
    public const string ResumeWindowExpired = "RESUME_WINDOW_EXPIRED";
    public const string HeartbeatLost = "HEARTBEAT_LOST";
    public const string LeaseExpired = "LEASE_EXPIRED";
    public const string BudgetExhausted = "BUDGET_EXHAUSTED";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>All 15 canonical v1.1 codes.</summary>
    public static readonly FrozenSet<string> All = new HashSet<string>
    {
        PermissionDenied, LeaseSubsetViolation, JobNotFound, DuplicateKey,
        AgentNotAvailable, AgentVersionNotAvailable, Cancelled, Timeout,
        ResumeWindowExpired, HeartbeatLost, LeaseExpired, BudgetExhausted,
        InvalidRequest, Unauthenticated, InternalError,
    }.ToFrozenSet();

    /// <summary>Retryable codes per spec §12: only <c>AGENT_NOT_AVAILABLE</c>, <c>TIMEOUT</c>,
    /// <c>HEARTBEAT_LOST</c>, and <c>INTERNAL_ERROR</c> may be retried.</summary>
    public static bool IsRetryable(string code) => code is
        AgentNotAvailable or Timeout or HeartbeatLost or InternalError;
}
