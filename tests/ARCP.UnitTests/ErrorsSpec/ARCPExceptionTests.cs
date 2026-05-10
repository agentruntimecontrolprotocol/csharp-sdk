using ARCP.Errors;
using ARCP.Ids;
using FluentAssertions;
using Xunit;

namespace ARCP.UnitTests.ErrorsSpec;

public class ARCPExceptionTests
{
    [Fact]
    public void DefaultsRetryableFromCanonicalTaxonomy()
    {
        new ARCPException(ErrorCode.Internal, "x").Retryable.Should().BeTrue();
        new ARCPException(ErrorCode.InvalidArgument, "x").Retryable.Should().BeFalse();
    }

    [Fact]
    public void ToPayloadAndFromPayloadRoundTrip()
    {
        ARCPException err = new(
            ErrorCode.PermissionDenied,
            "no",
            details: new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["resource"] = System.Text.Json.JsonDocument.Parse("\"file.txt\"").RootElement.Clone(),
            },
            traceId: "trace_xx");

        ErrorPayload payload = err.ToPayload();
        payload.Code.Should().Be(ErrorCode.PermissionDenied);
        payload.Message.Should().Be("no");
        payload.TraceId.Should().Be("trace_xx");
        payload.Details.Should().NotBeNull();

        ARCPException re = ARCPException.FromPayload(payload);
        re.Code.Should().Be(err.Code);
        re.Message.Should().Be(err.Message);
        re.TraceId.Should().Be(err.TraceId);
    }

    [Fact]
    public void CauseChainIsPreservedThroughInnerException()
    {
        var inner = new InvalidOperationException("root");
        var err = new InternalException("wrapped", inner);
        err.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void LeaseExpiredCarriesTypedContext()
    {
        var leaseId = LeaseId.New();
        var expiredAt = DateTimeOffset.UtcNow;
        var err = new LeaseExpiredException(leaseId, expiredAt);
        err.LeaseId.Should().Be(leaseId);
        err.ExpiredAt.Should().Be(expiredAt);
        err.Code.Should().Be(ErrorCode.LeaseExpired);
        err.Should().BeAssignableTo<PermissionDeniedException>();
    }

    [Fact]
    public void UnimplementedQuotesRfcSection()
    {
        var ex = new UnimplementedException("§14", "agent.delegate is deferred");
        ex.RfcSection.Should().Be("§14");
        ex.Detail.Should().Be("agent.delegate is deferred");
        ex.Message.Should().Contain("§14");
    }

    [Theory]
    [InlineData(ErrorCode.Ok, "OK")]
    [InlineData(ErrorCode.InvalidArgument, "INVALID_ARGUMENT")]
    [InlineData(ErrorCode.HeartbeatLost, "HEARTBEAT_LOST")]
    [InlineData(ErrorCode.BackpressureOverflow, "BACKPRESSURE_OVERFLOW")]
    public void ErrorCodeWireString(ErrorCode code, string expected)
    {
        code.ToWireString().Should().Be(expected);
    }

    [Fact]
    public void RateLimitedAliasResolvesToResourceExhausted()
    {
        ErrorCodes.RateLimited.Should().Be(ErrorCode.ResourceExhausted);
    }
}
