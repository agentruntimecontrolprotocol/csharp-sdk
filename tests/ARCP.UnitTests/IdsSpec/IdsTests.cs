using System.Text.Json;
using ARCP.Ids;
using FluentAssertions;
using Xunit;

namespace ARCP.UnitTests.IdsSpec;

public class IdsTests
{
    [Fact]
    public void NewSessionIdHasSessPrefix()
    {
        SessionId id = SessionId.New();
        id.Value.Should().StartWith("sess_");
    }

    [Fact]
    public void NewMessageIdHasMsgPrefix()
    {
        MessageId.New().Value.Should().StartWith("msg_");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FromStringRejectsEmptyOrNull(string? value)
    {
        Action act = () => SessionId.FromString(value!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RoundTripsAsBareJsonString()
    {
        SessionId id = new("sess_abc");
        string json = JsonSerializer.Serialize(id);
        json.Should().Be("\"sess_abc\"");

        SessionId back = JsonSerializer.Deserialize<SessionId>(json);
        back.Should().Be(id);
    }

    [Fact]
    public void DifferentIdTypesAreNotInterchangeable()
    {
        // The C# type system makes mixing a MessageId and SessionId a compile
        // error; this test documents that rather than asserts at runtime.
        MessageId msg = MessageId.New();
        SessionId sess = SessionId.New();
        msg.Value.Should().NotBe(sess.Value);
    }

    [Fact]
    public void ToStringReturnsValue()
    {
        JobId job = new("job_xxx");
        job.ToString().Should().Be("job_xxx");
    }

    [Fact]
    public void IdempotencyKeyHasNoNewFactory()
    {
        // IdempotencyKey is the only id without a New() factory — callers
        // supply the value (§6.4). This test pins that contract.
        typeof(IdempotencyKey).GetMethod("New").Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(SessionId))]
    [InlineData(typeof(MessageId))]
    [InlineData(typeof(JobId))]
    [InlineData(typeof(StreamId))]
    [InlineData(typeof(SubscriptionId))]
    [InlineData(typeof(TraceId))]
    [InlineData(typeof(SpanId))]
    [InlineData(typeof(LeaseId))]
    [InlineData(typeof(ArtifactId))]
    public void EveryGeneratedIdTypeHasNewFactory(Type idType)
    {
        idType.GetMethod("New", Type.EmptyTypes).Should().NotBeNull();
    }
}
