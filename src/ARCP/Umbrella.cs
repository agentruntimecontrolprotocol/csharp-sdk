// SPDX-License-Identifier: Apache-2.0
// Umbrella package: this assembly has no source of its own; it exists to bundle
// Arcp.Core, Arcp.Client, and Arcp.Runtime under one `dotnet add package Arcp`.
//
// Consumers `using Arcp.Client;` and `using Arcp.Runtime;` directly — the
// umbrella forwards by NuGet dependency, not C# type forwarding.

namespace Arcp;

/// <summary>Static metadata for the ARCP umbrella package.</summary>
public static class ArcpInfo
{
    /// <summary>The ARCP protocol version this SDK implements.</summary>
    public const string ProtocolVersion = "1.1";
}
