using ARCP;
using FluentAssertions;
using Xunit;

namespace ARCP.IntegrationTests;

/// <summary>
/// Trivial assertion that wiring is correct. Replaced by real tests in later phases.
/// </summary>
public class SmokeTest
{
    [Fact]
    public void ProtocolVersionIsExposed()
    {
        ProtocolVersion.Sdk.Should().StartWith("0.1.");
    }
}
