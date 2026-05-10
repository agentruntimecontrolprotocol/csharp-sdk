using System.Collections.Concurrent;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Permissions;

namespace ARCP.Runtime;

/// <summary>State of a single tracked lease.</summary>
public enum LeaseStatus
{
    /// <summary>Active and valid.</summary>
    Active,

    /// <summary>Expired naturally past <c>expires_at</c>.</summary>
    Expired,

    /// <summary>Revoked explicitly via <c>lease.revoked</c>.</summary>
    Revoked,
}

/// <summary>A snapshot of a lease's state.</summary>
/// <param name="Id">The lease id.</param>
/// <param name="Permission">The permission name.</param>
/// <param name="Resource">The resource scope.</param>
/// <param name="Operation">The operation scope.</param>
/// <param name="ExpiresAt">When the lease expires.</param>
/// <param name="Status">Current status.</param>
/// <param name="RevocationReason">Reason if revoked.</param>
public sealed record LeaseSnapshot(
    LeaseId Id,
    string Permission,
    string Resource,
    string Operation,
    DateTimeOffset ExpiresAt,
    LeaseStatus Status,
    string? RevocationReason = null);

/// <summary>
/// Tracks the lifecycle of leases granted by the runtime per RFC-0001-v2
/// §15.5. Operations attempted with revoked or expired leases throw
/// <see cref="LeaseExpiredException" /> / <see cref="LeaseRevokedException" />.
/// </summary>
public sealed class LeaseManager
{
    private sealed class LeaseRecord
    {
        public required LeaseId Id { get; init; }

        public required string Permission { get; init; }

        public required string Resource { get; init; }

        public required string Operation { get; init; }

        public DateTimeOffset ExpiresAt { get; set; }

        public bool Revoked { get; set; }

        public string? RevocationReason { get; set; }
    }

    private readonly ConcurrentDictionary<LeaseId, LeaseRecord> _leases = new();
    private readonly TimeProvider _time;

    /// <summary>Initializes a new <see cref="LeaseManager" />.</summary>
    /// <param name="time">Optional time provider.</param>
    public LeaseManager(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Issue a new lease. Returns the corresponding <c>lease.granted</c>
    /// payload (which the caller is responsible for sending on the wire).
    /// </summary>
    /// <param name="permission">Permission name.</param>
    /// <param name="resource">Resource scope.</param>
    /// <param name="operation">Operation scope.</param>
    /// <param name="duration">Lease duration.</param>
    /// <returns>The granted lease.</returns>
    public LeaseGranted Issue(
        string permission,
        string resource,
        string operation,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(operation);
        if (duration <= TimeSpan.Zero)
        {
            throw new InvalidArgumentException("Lease duration must be positive.");
        }

        LeaseId id = LeaseId.New();
        DateTimeOffset expiresAt = _time.GetUtcNow() + duration;
        LeaseRecord record = new()
        {
            Id = id,
            Permission = permission,
            Resource = resource,
            Operation = operation,
            ExpiresAt = expiresAt,
        };
        _leases[id] = record;
        return new LeaseGranted(id, permission, resource, operation, expiresAt);
    }

    /// <summary>Extend an existing lease by <paramref name="extension" />.</summary>
    /// <param name="id">Lease id.</param>
    /// <param name="extension">Time to extend by.</param>
    /// <returns>The new <see cref="LeaseExtended" /> payload.</returns>
    /// <exception cref="LeaseExpiredException">If the lease is already expired.</exception>
    /// <exception cref="LeaseRevokedException">If the lease is revoked.</exception>
    /// <exception cref="NotFoundException">If the lease id is unknown.</exception>
    public LeaseExtended Extend(LeaseId id, TimeSpan extension)
    {
        if (extension <= TimeSpan.Zero)
        {
            throw new InvalidArgumentException("Lease extension must be positive.");
        }
        LeaseRecord record = ResolveActive(id);
        record.ExpiresAt += extension;
        return new LeaseExtended(id, record.ExpiresAt);
    }

    /// <summary>Revoke a lease.</summary>
    /// <param name="id">Lease id.</param>
    /// <param name="reason">Revocation reason.</param>
    /// <returns>The corresponding <see cref="LeaseRevoked" /> payload.</returns>
    public LeaseRevoked Revoke(LeaseId id, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        if (!_leases.TryGetValue(id, out LeaseRecord? record))
        {
            throw new NotFoundException($"Lease {id} is not tracked.");
        }
        record.Revoked = true;
        record.RevocationReason = reason;
        return new LeaseRevoked(id, reason);
    }

    /// <summary>
    /// Verify that a lease is currently valid for the requested operation.
    /// Throws on revoked / expired / mismatched leases.
    /// </summary>
    /// <param name="id">Lease id.</param>
    /// <param name="permission">Required permission.</param>
    /// <param name="resource">Required resource scope.</param>
    /// <param name="operation">Required operation scope.</param>
    public void Check(LeaseId id, string permission, string resource, string operation)
    {
        LeaseRecord record = ResolveActive(id);
        if (!string.Equals(record.Permission, permission, StringComparison.Ordinal)
            || !string.Equals(record.Resource, resource, StringComparison.Ordinal)
            || !string.Equals(record.Operation, operation, StringComparison.Ordinal))
        {
            throw new PermissionDeniedException(
                $"Lease {id} does not authorize ({permission}, {resource}, {operation}).");
        }
    }

    /// <summary>Look up a lease snapshot.</summary>
    /// <param name="id">Lease id.</param>
    /// <returns>The current snapshot, or <see langword="null" /> if unknown.</returns>
    public LeaseSnapshot? Snapshot(LeaseId id)
    {
        if (!_leases.TryGetValue(id, out LeaseRecord? record))
        {
            return null;
        }
        return new LeaseSnapshot(
            record.Id,
            record.Permission,
            record.Resource,
            record.Operation,
            record.ExpiresAt,
            ComputeStatus(record),
            record.RevocationReason);
    }

    private LeaseRecord ResolveActive(LeaseId id)
    {
        if (!_leases.TryGetValue(id, out LeaseRecord? record))
        {
            throw new NotFoundException($"Lease {id} is not tracked.");
        }
        if (record.Revoked)
        {
            throw new LeaseRevokedException(id, record.RevocationReason ?? "revoked");
        }
        if (record.ExpiresAt <= _time.GetUtcNow())
        {
            throw new LeaseExpiredException(id, record.ExpiresAt);
        }
        return record;
    }

    private LeaseStatus ComputeStatus(LeaseRecord record)
    {
        if (record.Revoked) return LeaseStatus.Revoked;
        if (record.ExpiresAt <= _time.GetUtcNow()) return LeaseStatus.Expired;
        return LeaseStatus.Active;
    }
}
