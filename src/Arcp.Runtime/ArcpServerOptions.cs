// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Auth;
using Arcp.Runtime.Authorization;
using Arcp.Runtime.Credentials;

namespace Arcp.Runtime;

/// <summary>Configuration for <see cref="ArcpServer"/>.</summary>
public sealed class ArcpServerOptions
{
    /// <summary>Gets the runtime.</summary>
    public required Arcp.Core.Messages.RuntimeInfo Runtime { get; init; }

    /// <summary>Gets the auth.</summary>
    public IBearerVerifier? Auth { get; init; }

    /// <summary>Gets the features.</summary>
    public IReadOnlyList<string>? Features { get; init; }

    /// <summary>Gets the encodings.</summary>
    public IReadOnlyList<string>? Encodings { get; init; } = new[] { "json" };

    /// <summary>Gets the heartbeat interval sec.</summary>
    public int HeartbeatIntervalSec { get; init; } = 30;

    /// <summary>Gets the resume window sec.</summary>
    public int ResumeWindowSec { get; init; } = 600;

    /// <summary>How long an idempotency key remains valid for replay-with-identical-payload matching
    /// (spec §7.2). Submissions with the same key after this window create a fresh job.</summary>
    public int IdempotencyWindowSec { get; init; } = 3600;

    /// <summary>Whether a <c>cost.budget</c> exhaustion terminates the job with
    /// <c>BUDGET_EXHAUSTED</c> (legacy v1.0 behavior) or surfaces a non-fatal
    /// <c>tool_result.error</c> so the agent may emit a partial result and return normally
    /// (spec §9.6 SHOULD-preferred form). Default: <see langword="false"/>.</summary>
    public bool FatalBudgetExhaustion { get; init; }

    /// <summary>Gets the back pressure threshold.</summary>
    public int BackPressureThreshold { get; init; } = 1000;

    /// <summary>Gets the authorization policy.</summary>
    public IJobAuthorizationPolicy AuthorizationPolicy { get; init; } = new SamePrincipalPolicy();

    /// <summary>Gets the time provider.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets the credential provisioner.</summary>
    public ICredentialProvisioner? CredentialProvisioner { get; init; }

    /// <summary>Gets the credential store.</summary>
    public ICredentialStore CredentialStore { get; init; } = new InMemoryCredentialStore();
}
