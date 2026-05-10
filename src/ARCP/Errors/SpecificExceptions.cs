namespace ARCP.Errors;

/// <summary>§18.2 <c>UNAUTHENTICATED</c>. Missing or invalid credentials.</summary>
public sealed class UnauthenticatedException : ARCPException
{
    /// <summary>Initializes a new <see cref="UnauthenticatedException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public UnauthenticatedException(string message, Exception? cause = null)
        : base(ErrorCode.Unauthenticated, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>PERMISSION_DENIED</c>. Caller lacks required permission or lease.</summary>
public class PermissionDeniedException : ARCPException
{
    /// <summary>Initializes a new <see cref="PermissionDeniedException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public PermissionDeniedException(string message, Exception? cause = null)
        : base(ErrorCode.PermissionDenied, message, cause: cause)
    {
    }

    /// <summary>Reserved for subclasses that pin a more specific code.</summary>
    /// <param name="code">A specific code (e.g. <see cref="ErrorCode.LeaseExpired" />).</param>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    protected PermissionDeniedException(ErrorCode code, string message, Exception? cause = null)
        : base(code, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>LEASE_EXPIRED</c>. Operation attempted with expired lease (§15.5).</summary>
public sealed class LeaseExpiredException : PermissionDeniedException
{
    /// <summary>Initializes a new <see cref="LeaseExpiredException" />.</summary>
    /// <param name="leaseId">The expired lease id.</param>
    /// <param name="expiredAt">The instant at which the lease expired.</param>
    /// <param name="cause">Optional inner exception.</param>
    public LeaseExpiredException(Ids.LeaseId leaseId, DateTimeOffset expiredAt, Exception? cause = null)
        : base(ErrorCode.LeaseExpired, $"Lease {leaseId} expired at {expiredAt:O}.", cause)
    {
        LeaseId = leaseId;
        ExpiredAt = expiredAt;
    }

    /// <summary>The lease that has expired.</summary>
    public Ids.LeaseId LeaseId { get; }

    /// <summary>The instant the lease expired.</summary>
    public DateTimeOffset ExpiredAt { get; }
}

/// <summary>§18.2 <c>LEASE_REVOKED</c>. Operation attempted with revoked lease.</summary>
public sealed class LeaseRevokedException : PermissionDeniedException
{
    /// <summary>Initializes a new <see cref="LeaseRevokedException" />.</summary>
    /// <param name="leaseId">The revoked lease id.</param>
    /// <param name="reason">The reason given for revocation.</param>
    /// <param name="cause">Optional inner exception.</param>
    public LeaseRevokedException(Ids.LeaseId leaseId, string reason, Exception? cause = null)
        : base(ErrorCode.LeaseRevoked, $"Lease {leaseId} revoked: {reason}.", cause)
    {
        LeaseId = leaseId;
        Reason = reason;
    }

    /// <summary>The lease that was revoked.</summary>
    public Ids.LeaseId LeaseId { get; }

    /// <summary>The reason given for revocation.</summary>
    public string Reason { get; }
}

/// <summary>§18.2 <c>INVALID_ARGUMENT</c>. Caller passed a malformed or invalid argument.</summary>
public sealed class InvalidArgumentException : ARCPException
{
    /// <summary>Initializes a new <see cref="InvalidArgumentException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public InvalidArgumentException(string message, Exception? cause = null)
        : base(ErrorCode.InvalidArgument, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>NOT_FOUND</c>. Referenced entity does not exist.</summary>
public sealed class NotFoundException : ARCPException
{
    /// <summary>Initializes a new <see cref="NotFoundException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public NotFoundException(string message, Exception? cause = null)
        : base(ErrorCode.NotFound, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>FAILED_PRECONDITION</c>. Pre-condition unmet.</summary>
public sealed class FailedPreconditionException : ARCPException
{
    /// <summary>Initializes a new <see cref="FailedPreconditionException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public FailedPreconditionException(string message, Exception? cause = null)
        : base(ErrorCode.FailedPrecondition, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>DEADLINE_EXCEEDED</c>. Operation timed out before completion.</summary>
public sealed class DeadlineExceededException : ARCPException
{
    /// <summary>Initializes a new <see cref="DeadlineExceededException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public DeadlineExceededException(string message, Exception? cause = null)
        : base(ErrorCode.DeadlineExceeded, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>CANCELLED</c>. Operation cancelled.</summary>
public sealed class CancelledException : ARCPException
{
    /// <summary>Initializes a new <see cref="CancelledException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public CancelledException(string message, Exception? cause = null)
        : base(ErrorCode.Cancelled, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>ABORTED</c>. Concurrency conflict or hard termination.</summary>
public sealed class AbortedException : ARCPException
{
    /// <summary>Initializes a new <see cref="AbortedException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public AbortedException(string message, Exception? cause = null)
        : base(ErrorCode.Aborted, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>HEARTBEAT_LOST</c>. Job missed required heartbeats (§10.3).</summary>
public sealed class HeartbeatLostException : ARCPException
{
    /// <summary>Initializes a new <see cref="HeartbeatLostException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="jobId">The job that lost heartbeats.</param>
    public HeartbeatLostException(string message, Ids.JobId jobId)
        : base(ErrorCode.HeartbeatLost, message)
    {
        JobId = jobId;
    }

    /// <summary>The job that lost heartbeats.</summary>
    public Ids.JobId JobId { get; }
}

/// <summary>
/// §18.2 <c>UNIMPLEMENTED</c>. Feature not supported by this runtime.
/// Always carries the RFC section reference where the unsupported behavior is
/// defined, so consumers can quickly look up what is missing.
/// </summary>
public sealed class UnimplementedException : ARCPException
{
    /// <summary>Initializes a new <see cref="UnimplementedException" />.</summary>
    /// <param name="rfcSection">The RFC-0001-v2 section this feature is defined in (e.g. <c>"§10.6"</c>).</param>
    /// <param name="detail">Human-readable detail about the missing surface.</param>
    public UnimplementedException(string rfcSection, string detail)
        : base(ErrorCode.Unimplemented, $"{rfcSection}: {detail}")
    {
        RfcSection = rfcSection;
        Detail = detail;
    }

    /// <summary>RFC-0001-v2 section reference where the missing feature is defined.</summary>
    public string RfcSection { get; }

    /// <summary>Detail message describing what is missing.</summary>
    public string Detail { get; }
}

/// <summary>§18.2 <c>INTERNAL</c>. Internal runtime error.</summary>
public sealed class InternalException : ARCPException
{
    /// <summary>Initializes a new <see cref="InternalException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public InternalException(string message, Exception? cause = null)
        : base(ErrorCode.Internal, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>BACKPRESSURE_OVERFLOW</c>. Subscription or stream dropped.</summary>
public sealed class BackpressureOverflowException : ARCPException
{
    /// <summary>Initializes a new <see cref="BackpressureOverflowException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public BackpressureOverflowException(string message, Exception? cause = null)
        : base(ErrorCode.BackpressureOverflow, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>DATA_LOSS</c>. Unrecoverable data loss or corruption.</summary>
public sealed class DataLossException : ARCPException
{
    /// <summary>Initializes a new <see cref="DataLossException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public DataLossException(string message, Exception? cause = null)
        : base(ErrorCode.DataLoss, message, cause: cause)
    {
    }
}

/// <summary>§18.2 <c>UNAVAILABLE</c>. Transient unavailability; retry MAY succeed.</summary>
public sealed class UnavailableException : ARCPException
{
    /// <summary>Initializes a new <see cref="UnavailableException" />.</summary>
    /// <param name="message">Human-readable detail.</param>
    /// <param name="cause">Optional inner exception.</param>
    public UnavailableException(string message, Exception? cause = null)
        : base(ErrorCode.Unavailable, message, cause: cause)
    {
    }
}
