// SPDX-License-Identifier: Apache-2.0
using System;

namespace Arcp.Core.Errors;

/// <summary>The base ARCP error type. Carries the spec error <see cref="Code"/> string and the
/// <see cref="Retryable"/> flag per spec §12.</summary>
public class ArcpException : Exception
{
    public string Code { get; }

    public bool Retryable { get; }

    public string? Detail { get; }

    public ArcpException(string code, string message, string? detail = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Retryable = ErrorCode.IsRetryable(code);
        Detail = detail;
    }
}

public sealed class PermissionDeniedException : ArcpException
{
    public PermissionDeniedException(string message, string? detail = null) : base(ErrorCode.PermissionDenied, message, detail) { }
}

public sealed class LeaseSubsetViolationException : ArcpException
{
    public LeaseSubsetViolationException(string message, string? detail = null) : base(ErrorCode.LeaseSubsetViolation, message, detail) { }
}

public sealed class JobNotFoundException : ArcpException
{
    public JobNotFoundException(string message, string? detail = null) : base(ErrorCode.JobNotFound, message, detail) { }
}

public sealed class DuplicateKeyException : ArcpException
{
    public DuplicateKeyException(string message, string? detail = null) : base(ErrorCode.DuplicateKey, message, detail) { }
}

public sealed class AgentNotAvailableException : ArcpException
{
    public AgentNotAvailableException(string message, string? detail = null) : base(ErrorCode.AgentNotAvailable, message, detail) { }
}

public sealed class AgentVersionNotAvailableException : ArcpException
{
    public AgentVersionNotAvailableException(string message, string? detail = null) : base(ErrorCode.AgentVersionNotAvailable, message, detail) { }
}

public sealed class CancelledException : ArcpException
{
    public CancelledException(string message, string? detail = null) : base(ErrorCode.Cancelled, message, detail) { }
}

public sealed class TimeoutException : ArcpException
{
    public TimeoutException(string message, string? detail = null) : base(ErrorCode.Timeout, message, detail) { }
}

public sealed class ResumeWindowExpiredException : ArcpException
{
    public ResumeWindowExpiredException(string message, string? detail = null) : base(ErrorCode.ResumeWindowExpired, message, detail) { }
}

public sealed class HeartbeatLostException : ArcpException
{
    public HeartbeatLostException(string message, string? detail = null) : base(ErrorCode.HeartbeatLost, message, detail) { }
}

public sealed class LeaseExpiredException : ArcpException
{
    public LeaseExpiredException(string message, string? detail = null) : base(ErrorCode.LeaseExpired, message, detail) { }
}

public sealed class BudgetExhaustedException : ArcpException
{
    public BudgetExhaustedException(string message, string? detail = null) : base(ErrorCode.BudgetExhausted, message, detail) { }
}

public sealed class InvalidRequestException : ArcpException
{
    public InvalidRequestException(string message, string? detail = null) : base(ErrorCode.InvalidRequest, message, detail) { }
}

public sealed class UnauthenticatedException : ArcpException
{
    public UnauthenticatedException(string message, string? detail = null) : base(ErrorCode.Unauthenticated, message, detail) { }
}

public sealed class InternalErrorException : ArcpException
{
    public InternalErrorException(string message, string? detail = null) : base(ErrorCode.InternalError, message, detail) { }
}
