// SPDX-License-Identifier: Apache-2.0
using System;

namespace Arcp.Core.Errors;

/// <summary>The base ARCP error type. Carries the spec error <see cref="Code"/> string and the
/// <see cref="Retryable"/> flag per spec §12.</summary>
public class ArcpException : Exception
{
    /// <summary>Gets the code.</summary>
    public string Code { get; }

    /// <summary>Gets the retryable.</summary>
    public bool Retryable { get; }

    /// <summary>Gets the detail.</summary>
    public string? Detail { get; }

    /// <summary>Initializes a new instance of the <see cref="ArcpException"/> class.</summary>
    public ArcpException(string code, string message, string? detail = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Retryable = ErrorCode.IsRetryable(code);
        Detail = detail;
    }
}

/// <summary>Gets the permission denied exception.</summary>
public sealed class PermissionDeniedException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="PermissionDeniedException"/> class.</summary>
    public PermissionDeniedException(string message, string? detail = null) : base(ErrorCode.PermissionDenied, message, detail) { }
}

/// <summary>Gets the lease subset violation exception.</summary>
public sealed class LeaseSubsetViolationException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="LeaseSubsetViolationException"/> class.</summary>
    public LeaseSubsetViolationException(string message, string? detail = null) : base(ErrorCode.LeaseSubsetViolation, message, detail) { }
}

/// <summary>Gets the job not found exception.</summary>
public sealed class JobNotFoundException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="JobNotFoundException"/> class.</summary>
    public JobNotFoundException(string message, string? detail = null) : base(ErrorCode.JobNotFound, message, detail) { }
}

/// <summary>Gets the duplicate key exception.</summary>
public sealed class DuplicateKeyException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="DuplicateKeyException"/> class.</summary>
    public DuplicateKeyException(string message, string? detail = null) : base(ErrorCode.DuplicateKey, message, detail) { }
}

/// <summary>Gets the agent not available exception.</summary>
public sealed class AgentNotAvailableException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="AgentNotAvailableException"/> class.</summary>
    public AgentNotAvailableException(string message, string? detail = null) : base(ErrorCode.AgentNotAvailable, message, detail) { }
}

/// <summary>Gets the agent version not available exception.</summary>
public sealed class AgentVersionNotAvailableException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="AgentVersionNotAvailableException"/> class.</summary>
    public AgentVersionNotAvailableException(string message, string? detail = null) : base(ErrorCode.AgentVersionNotAvailable, message, detail) { }
}

/// <summary>Gets the cancelled exception.</summary>
public sealed class CancelledException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="CancelledException"/> class.</summary>
    public CancelledException(string message, string? detail = null) : base(ErrorCode.Cancelled, message, detail) { }
}

/// <summary>Gets the timeout exception.</summary>
public sealed class TimeoutException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="TimeoutException"/> class.</summary>
    public TimeoutException(string message, string? detail = null) : base(ErrorCode.Timeout, message, detail) { }
}

/// <summary>Gets the resume window expired exception.</summary>
public sealed class ResumeWindowExpiredException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="ResumeWindowExpiredException"/> class.</summary>
    public ResumeWindowExpiredException(string message, string? detail = null) : base(ErrorCode.ResumeWindowExpired, message, detail) { }
}

/// <summary>Gets the heartbeat lost exception.</summary>
public sealed class HeartbeatLostException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="HeartbeatLostException"/> class.</summary>
    public HeartbeatLostException(string message, string? detail = null) : base(ErrorCode.HeartbeatLost, message, detail) { }
}

/// <summary>Gets the lease expired exception.</summary>
public sealed class LeaseExpiredException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="LeaseExpiredException"/> class.</summary>
    public LeaseExpiredException(string message, string? detail = null) : base(ErrorCode.LeaseExpired, message, detail) { }
}

/// <summary>Gets the budget exhausted exception.</summary>
public sealed class BudgetExhaustedException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="BudgetExhaustedException"/> class.</summary>
    public BudgetExhaustedException(string message, string? detail = null) : base(ErrorCode.BudgetExhausted, message, detail) { }
}

/// <summary>Gets the invalid request exception.</summary>
public sealed class InvalidRequestException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidRequestException"/> class.</summary>
    public InvalidRequestException(string message, string? detail = null) : base(ErrorCode.InvalidRequest, message, detail) { }
}

/// <summary>Gets the unauthenticated exception.</summary>
public sealed class UnauthenticatedException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="UnauthenticatedException"/> class.</summary>
    public UnauthenticatedException(string message, string? detail = null) : base(ErrorCode.Unauthenticated, message, detail) { }
}

/// <summary>Gets the internal error exception.</summary>
public sealed class InternalErrorException : ArcpException
{
    /// <summary>Initializes a new instance of the <see cref="InternalErrorException"/> class.</summary>
    public InternalErrorException(string message, string? detail = null) : base(ErrorCode.InternalError, message, detail) { }
}
