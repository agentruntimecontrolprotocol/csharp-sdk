// SPDX-License-Identifier: Apache-2.0
using System;
using Xunit;
using Xunit.Sdk;

namespace Arcp.ConformanceTests;

/// <summary>A <see cref="FactAttribute"/> whose <see cref="FactAttribute.DisplayName"/> embeds the
/// spec § citation. Failing assertions report like a spec compliance report.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConformanceFactAttribute : FactAttribute
{
    public ConformanceFactAttribute(string specSection, string requirement)
    {
        DisplayName = $"{specSection}: {requirement}";
    }
}
