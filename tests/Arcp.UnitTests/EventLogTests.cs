// SPDX-License-Identifier: Apache-2.0
using Arcp.Core.Messages;
using Arcp.Core.Store;
using Arcp.Core.Wire;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class EventLogTests
{
    [Fact]
    public void EventLog_assigns_monotonic_session_scoped_event_seq()
    {
        var log = new EventLog();
        var a = log.Append(new Envelope { Type = MessageTypeNames.JobEvent, Payload = new JobEventPayload { Kind = "log" } });
        var b = log.Append(new Envelope { Type = MessageTypeNames.JobEvent, Payload = new JobEventPayload { Kind = "log" } });
        var c = log.Append(new Envelope { Type = MessageTypeNames.JobEvent, Payload = new JobEventPayload { Kind = "log" } });
        a.EventSeq.Should().Be(1);
        b.EventSeq.Should().Be(2);
        c.EventSeq.Should().Be(3);
    }

    [Fact]
    public void EventLog_ReadFrom_yields_only_seq_greater_than()
    {
        var log = new EventLog();
        for (var i = 0; i < 5; i++)
        {
            log.Append(new Envelope { Type = MessageTypeNames.JobEvent, Payload = new JobEventPayload { Kind = "log" } });
        }
        log.ReadFrom(2).Should().HaveCount(3);
    }

    [Fact]
    public void EventLog_trim_is_advisory()
    {
        var log = new EventLog();
        log.Append(new Envelope { Type = MessageTypeNames.JobEvent, Payload = new JobEventPayload { Kind = "log" } });
        log.Append(new Envelope { Type = MessageTypeNames.JobEvent, Payload = new JobEventPayload { Kind = "log" } });
        log.Trim(1);
        log.LastAckedSeq.Should().Be(1);
    }
}
