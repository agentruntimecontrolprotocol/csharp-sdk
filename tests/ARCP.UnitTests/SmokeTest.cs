using ARCP;
using FluentAssertions;
using Xunit;

namespace ARCP.UnitTests;

/// <summary>
/// Trivial assertion that wiring is correct. Replaced by real tests in Phase 1.
/// </summary>
public class SmokeTest
{
    [Fact]
    public void ProtocolVersionIsExposed()
    {
        ProtocolVersion.Wire.Should().Be("1.0");
    }
}
