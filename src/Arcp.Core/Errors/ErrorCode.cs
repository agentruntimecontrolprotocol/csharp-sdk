// SPDX-License-Identifier: Apache-2.0
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Arcp.Core.Errors;

/// <summary>Canonical ARCP v1.1 error codes (spec §12). Strings, not an enum, so unknown
/// deployment-specific codes round-trip on the wire.</summary>
public static class ErrorCode
{
    /// <summary>Gets the permission denied.</summary>
    public const string PermissionDenied = "PERMISSION_DENIED";
    /// <summary>Gets the lease subset violation.</summary>
    public const string LeaseSubsetViolation = "LEASE_SUBSET_VIOLATION";
    /// <summary>Gets the job not found.</summary>
    public const string JobNotFound = "JOB_NOT_FOUND";
    /// <summary>Gets the duplicate key.</summary>
    public const string DuplicateKey = "DUPLICATE_KEY";
    /// <summary>Gets the agent not available.</summary>
    public const string AgentNotAvailable = "AGENT_NOT_AVAILABLE";
    /// <summary>Gets the agent version not available.</summary>
    public const string AgentVersionNotAvailable = "AGENT_VERSION_NOT_AVAILABLE";
    /// <summary>Gets the cancelled.</summary>
    public const string Cancelled = "CANCELLED";
    /// <summary>Gets the timeout.</summary>
    public const string Timeout = "TIMEOUT";
    /// <summary>Gets the resume window expired.</summary>
    public const string ResumeWindowExpired = "RESUME_WINDOW_EXPIRED";
    /// <summary>Gets the heartbeat lost.</summary>
    public const string HeartbeatLost = "HEARTBEAT_LOST";
    /// <summary>Gets the lease expired.</summary>
    public const string LeaseExpired = "LEASE_EXPIRED";
    /// <summary>Gets the budget exhausted.</summary>
    public const string BudgetExhausted = "BUDGET_EXHAUSTED";
    /// <summary>Gets the invalid request.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";
    /// <summary>Gets the unauthenticated.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";
    /// <summary>Gets the internal error.</summary>
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
