// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Caps;
using Arcp.Core.Messages;

namespace Arcp.Client;

/// <summary>Configuration for <see cref="ArcpClient"/>.</summary>
public sealed class ArcpClientOptions
{
    public required ClientInfo Client { get; init; }

    public string? Token { get; init; }

    public string AuthScheme { get; init; } = "bearer";

    public IReadOnlyList<string>? Features { get; init; } = FeatureSet.AllFeatures;

    public IReadOnlyList<string>? Encodings { get; init; } = new[] { "json" };

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
