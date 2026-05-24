// SPDX-License-Identifier: Apache-2.0
using System;
using Arcp.Core.Ids;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class IdentifiersTests
{
    [Fact]
    public void MessageId_round_trips()
    {
        var a = MessageId.New();
        a.ToString().Should().StartWith("msg_");
        MessageId.Parse(a.Value).Should().Be(a);
        MessageId.TryParse(a.Value, null, out var parsed).Should().BeTrue();
        parsed.Should().Be(a);
    }

    [Fact]
    public void SessionId_round_trips()
    {
        var a = SessionId.New();
        a.ToString().Should().StartWith("sess_");
        SessionId.Parse(a.Value).Should().Be(a);
        SessionId.TryParse(a.Value, null, out var parsed).Should().BeTrue();
        parsed.Should().Be(a);
    }

    [Fact]
    public void JobId_round_trips()
    {
        var a = JobId.New();
        a.ToString().Should().StartWith("job_");
        JobId.Parse(a.Value).Should().Be(a);
        JobId.TryParse(a.Value, null, out var parsed).Should().BeTrue();
        parsed.Should().Be(a);
    }

    [Fact]
    public void TraceId_round_trips()
    {
        var a = TraceId.New();
        TraceId.Parse(a.Value).Should().Be(a);
        TraceId.TryParse(a.Value, null, out var parsed).Should().BeTrue();
        parsed.Should().Be(a);
    }

    [Fact]
    public void Parse_null_throws_for_each_id()
    {
        var actMsg = () => MessageId.Parse(null!);
        actMsg.Should().Throw<ArgumentNullException>();
        var actSess = () => SessionId.Parse(null!);
        actSess.Should().Throw<ArgumentNullException>();
        var actJob = () => JobId.Parse(null!);
        actJob.Should().Throw<ArgumentNullException>();
        var actTrace = () => TraceId.Parse(null!);
        actTrace.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParse_empty_returns_false_for_each_id()
    {
        MessageId.TryParse("", null, out _).Should().BeFalse();
        SessionId.TryParse(null, null, out _).Should().BeFalse();
        JobId.TryParse("", null, out _).Should().BeFalse();
        TraceId.TryParse(null, null, out _).Should().BeFalse();
    }

    [Fact]
    public void SpanId_new_is_16_hex_lower()
    {
        var sid = SpanId.New();
        sid.Value.Length.Should().Be(16);
        sid.Value.Should().MatchRegex("^[0-9a-f]{16}$");
        sid.ToString().Should().Be(sid.Value);
    }

    [Fact]
    public void ResultId_and_ArtifactId_prefixes()
    {
        ResultId.New().Value.Should().StartWith("res_");
        ResultId.New().ToString().Should().StartWith("res_");
        ArtifactId.New().Value.Should().StartWith("art_");
        ArtifactId.New().ToString().Should().StartWith("art_");
    }
}
