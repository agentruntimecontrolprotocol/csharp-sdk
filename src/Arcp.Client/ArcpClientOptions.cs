// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using Arcp.Core.Caps;
using Arcp.Core.Messages;

namespace Arcp.Client;

/// <summary>Configuration for <see cref="ArcpClient"/>.</summary>
public sealed class ArcpClientOptions
{
    /// <summary>Gets the client.</summary>
    public required ClientInfo Client { get; init; }

    /// <summary>Gets the token.</summary>
    public string? Token { get; init; }

    /// <summary>Gets the auth scheme.</summary>
    public string AuthScheme { get; init; } = "bearer";

    /// <summary>Gets the features.</summary>
    public IReadOnlyList<string>? Features { get; init; } = FeatureSet.AllFeatures;

    /// <summary>Gets the encodings.</summary>
    public IReadOnlyList<string>? Encodings { get; init; } = new[] { "json" };

    /// <summary>Gets the time provider.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
