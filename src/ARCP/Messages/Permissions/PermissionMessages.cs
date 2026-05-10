using ARCP.Envelope;
using ARCP.Ids;

namespace ARCP.Messages.Permissions;

/// <summary>§15.4 runtime asks for a permission grant.</summary>
public sealed record PermissionRequest(
    string Permission,
    string Resource,
    string Operation,
    string? Reason = null,
    int? RequestedLeaseSeconds = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "permission.request";
}

/// <summary>§15.4 client &gt; runtime: permission granted.</summary>
public sealed record PermissionGrant(
    string Permission,
    string Resource,
    string Operation,
    LeaseId? LeaseId = null,
    string? GrantedBy = null,
    DateTimeOffset? ExpiresAt = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "permission.grant";
}

/// <summary>§15.4 client &gt; runtime: permission denied.</summary>
public sealed record PermissionDeny(
    string Permission,
    string Resource,
    string Operation,
    string Reason) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "permission.deny";
}

/// <summary>§15.5 lease granted.</summary>
public sealed record LeaseGranted(
    LeaseId LeaseId,
    string Permission,
    string Resource,
    string Operation,
    DateTimeOffset ExpiresAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "lease.granted";
}

/// <summary>§15.5 holder requests lease extension.</summary>
public sealed record LeaseRefresh(
    LeaseId LeaseId,
    int RequestedSeconds) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "lease.refresh";
}

/// <summary>§15.5 lease extended.</summary>
public sealed record LeaseExtended(
    LeaseId LeaseId,
    DateTimeOffset ExpiresAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "lease.extended";
}

/// <summary>§15.5 lease revoked.</summary>
public sealed record LeaseRevoked(
    LeaseId LeaseId,
    string Reason) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "lease.revoked";
}
