using ARCP;
using FluentAssertions;
using Xunit;

namespace ARCP.IntegrationTests;

/// <summary>
/// Placeholder so this project has at least one runnable test before Phase 2
/// adds real integration coverage.
/// </summary>
public class Phase1Placeholder
{
    [Fact]
    public void ProtocolVersionWireMatchesSpec() =>
        ProtocolVersion.Wire.Should().Be("1.0");
}
