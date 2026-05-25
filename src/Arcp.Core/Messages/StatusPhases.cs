// SPDX-License-Identifier: Apache-2.0

namespace Arcp.Core.Messages;

/// <summary>Well-known phases for <c>status</c> job events.</summary>
public static class StatusPhases
{
    /// <summary>Gets the credential rotated.</summary>
    public const string CredentialRotated = "credential_rotated";

    /// <summary>Emitted by the runtime when a job's lease has expired (spec §9.5). The terminal
    /// <c>job.error</c> with code <c>LEASE_EXPIRED</c> and <c>final_status:"error"</c> follows.</summary>
    public const string LeaseExpired = "lease_expired";
}
