// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Ids;

namespace Arcp.Runtime.Credentials;

/// <summary>Context passed to a credential provisioner when issuing job-scoped credentials.</summary>
public sealed record CredentialIssueContext(
    JobId JobId,
    SessionId SessionId,
    string? SubmitterPrincipal,
    string? ParentJobId,
    IReadOnlyDictionary<string, decimal>? RemainingBudget);
