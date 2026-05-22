// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Arcp.Core.Caps;
using Arcp.Core.Messages;
using Arcp.Core.Wire;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class EnvelopeTests
{
    [Fact]
    public void Envelope_round_trips_session_hello()
    {
        var env = new Envelope
        {
            Type = MessageTypeNames.SessionHello,
            Payload = new SessionHelloPayload
            {
                Client = new ClientInfo { Name = "examplectl", Version = "0.4.1" },
                Capabilities = new Capabilities { Features = new[] { "heartbeat", "ack" } },
            },
        };
        var json = ArcpJson.Serialize(env);
        var roundTrip = ArcpJson.Deserialize(json);
        roundTrip.Type.Should().Be(MessageTypeNames.SessionHello);
        roundTrip.Payload.Should().BeOfType<SessionHelloPayload>();
        ((SessionHelloPayload)roundTrip.Payload!).Client.Name.Should().Be("examplectl");
    }

    [Fact]
    public void Envelope_preserves_unknown_top_level_fields()
    {
        const string Json = "{\"arcp\":\"1.1\",\"id\":\"msg_x\",\"type\":\"session.bye\",\"payload\":{},\"x_extra\":42}";
        var env = ArcpJson.Deserialize(Json);
        env.Extensions.Should().NotBeNull();
        env.Extensions!.Should().ContainKey("x_extra");
        var out_ = ArcpJson.Serialize(env);
        out_.Should().Contain("x_extra");
    }

    [Fact]
    public void Envelope_rejects_unsupported_arcp_version()
    {
        const string Json = "{\"arcp\":\"2\",\"id\":\"x\",\"type\":\"session.bye\",\"payload\":{}}";
        var act = () => ArcpJson.Deserialize(Json);
        act.Should().Throw<Arcp.Core.Errors.InvalidRequestException>();
    }

    [Fact]
    public void Feature_intersection_drops_unilateral_features()
    {
        var a = new[] { "heartbeat", "ack", "subscribe" };
        var b = new[] { "ack", "subscribe", "result_chunk" };
        FeatureSet.Intersect(a, b).Should().BeEquivalentTo(new[] { "ack", "subscribe" });
    }
}
