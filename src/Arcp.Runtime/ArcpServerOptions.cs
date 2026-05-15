// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Runtime.Authorization;

namespace Arcp.Runtime;

/// <summary>Configuration for <see cref="ArcpServer"/>.</summary>
public sealed class ArcpServerOptions
{
    public required Arcp.Core.Messages.RuntimeInfo Runtime { get; init; }

    public IBearerVerifier? Auth { get; init; }

    public IReadOnlyList<string>? Features { get; init; } = FeatureSet.AllFeatures;

    public IReadOnlyList<string>? Encodings { get; init; } = new[] { "json" };

    public int HeartbeatIntervalSec { get; init; } = 30;

    public int ResumeWindowSec { get; init; } = 600;

    public int BackPressureThreshold { get; init; } = 1000;

    public IJobAuthorizationPolicy AuthorizationPolicy { get; init; } = new SamePrincipalPolicy();

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
