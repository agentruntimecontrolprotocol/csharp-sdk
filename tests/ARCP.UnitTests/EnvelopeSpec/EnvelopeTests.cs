using System.Text.Json;
using ARCP.Envelope;
using ARCP.Ids;
using FluentAssertions;
using Xunit;

namespace ARCP.UnitTests.EnvelopeSpec;

public class EnvelopeTests
{
    [Fact]
    public void RoundTripsThroughJsonSerializer()
    {
        SessionId session = SessionId.New();
        Envelope.Envelope env = new()
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.FromString("msg_test"),
            Type = "ping",
            Timestamp = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero),
            Payload = new Diagnostic.PingPayload(),
            SessionId = session,
            Priority = Priority.Normal,
        };

        string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);

        // Top-level type must be present at envelope level (§6.1.1).
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("ping");
        doc.RootElement.GetProperty("session_id").GetString().Should().Be(session.Value);
        doc.RootElement.GetProperty("priority").GetString().Should().Be("normal");

        Envelope.Envelope back = JsonSerializer.Deserialize<Envelope.Envelope>(json, EnvelopeJson.Options)!;
        back.Should().NotBeNull();
        back.Type.Should().Be("ping");
        back.Id.Should().Be(env.Id);
        back.SessionId.Should().Be(env.SessionId);
        back.Payload.Should().BeOfType<Diagnostic.PingPayload>();
        back.Priority.Should().Be(Priority.Normal);
    }

    [Fact]
    public void DeserializeRejectsUnknownType()
    {
        string json = """
            {
              "arcp": "1.0",
              "id": "msg_x",
              "type": "totally.bogus",
              "timestamp": "2026-05-09T12:00:00.000+00:00",
              "payload": {}
            }
            """;
        Action act = () => JsonSerializer.Deserialize<Envelope.Envelope>(json, EnvelopeJson.Options);
        act.Should().Throw<Errors.UnimplementedException>();
    }

    [Fact]
    public void DeserializeRejectsMissingRequiredField()
    {
        string json = """
            {
              "id": "msg_x",
              "type": "ping",
              "timestamp": "2026-05-09T12:00:00.000+00:00",
              "payload": {}
            }
            """;
        Action act = () => JsonSerializer.Deserialize<Envelope.Envelope>(json, EnvelopeJson.Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void TimestampIsEmittedInRfc3339UtcWithMillis()
    {
        Envelope.Envelope env = new()
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.FromString("msg_x"),
            Type = "ping",
            Timestamp = new DateTimeOffset(2026, 5, 9, 12, 30, 45, 123, TimeSpan.FromHours(-7)),
            Payload = new Diagnostic.PingPayload(),
        };
        string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);
        using JsonDocument doc = JsonDocument.Parse(json);
        // Converted to UTC: 19:30:45.123Z.
        doc.RootElement.GetProperty("timestamp").GetString().Should().Be("2026-05-09T19:30:45.123Z");
    }

    [Fact]
    public void OptionalFieldsAreOmittedWhenAbsent()
    {
        Envelope.Envelope env = new()
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.FromString("msg_x"),
            Type = "ping",
            Timestamp = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero),
            Payload = new Diagnostic.PingPayload(),
        };
        string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("session_id", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("priority", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("trace_id", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("extensions", out _).Should().BeFalse();
    }

    [Fact]
    public void BuilderProducesValidEnvelopeWithDefaults()
    {
        Envelope.Envelope env = Envelope.Envelope.Builder.Build(new Diagnostic.PingPayload());
        env.Arcp.Should().Be(ProtocolVersion.Wire);
        env.Type.Should().Be("ping");
        env.Id.Value.Should().StartWith("msg_");
        env.Payload.WireType.Should().Be("ping");
    }

    [Fact]
    public void ExtensionsRoundTripPreserved()
    {
        var extensions = new Dictionary<string, JsonElement>
        {
            ["optional"] = JsonDocument.Parse("true").RootElement.Clone(),
            ["arcpx.acme.feature.v1"] = JsonDocument.Parse("\"hello\"").RootElement.Clone(),
        };
        Envelope.Envelope env = new()
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.FromString("msg_e"),
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Diagnostic.PingPayload(),
            Extensions = extensions,
        };
        string json = JsonSerializer.Serialize(env, EnvelopeJson.Options);
        Envelope.Envelope back = JsonSerializer.Deserialize<Envelope.Envelope>(json, EnvelopeJson.Options)!;
        back.Extensions.Should().NotBeNull();
        back.Extensions!.Keys.Should().Contain("arcpx.acme.feature.v1");
    }
}
