using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Store;
using FluentAssertions;
using Xunit;

namespace ARCP.UnitTests.StoreSpec;

public class EventLogTests
{
    private static Envelope.Envelope MakePing(MessageId? id = null)
    {
        return new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = id ?? MessageId.New(),
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Messages.Control.Ping(),
        };
    }

    [Fact]
    public async Task AppendIsIdempotentByMessageId()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();
        var env = MakePing();

        var first = await log.AppendAsync(session, env);
        var second = await log.AppendAsync(session, env);

        first.Should().Be(EventLogAppendResult.Appended);
        second.Should().Be(EventLogAppendResult.Duplicate);

        (await log.CountAsync(session)).Should().Be(1);
    }

    [Fact]
    public async Task ReplayReturnsCanonicalOrder()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();
        var first = MakePing();
        var second = MakePing();
        var third = MakePing();

        await log.AppendAsync(session, first);
        await log.AppendAsync(session, second);
        await log.AppendAsync(session, third);

        var ordered = new List<MessageId>();
        await foreach (var entry in log.ReplayAsync(session))
        {
            ordered.Add(entry.MessageId);
        }

        ordered.Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public async Task ReplayAfterMessageIdSkipsAnchorAndPredecessors()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();
        var first = MakePing();
        var second = MakePing();
        var third = MakePing();
        await log.AppendAsync(session, first);
        await log.AppendAsync(session, second);
        await log.AppendAsync(session, third);

        var got = new List<MessageId>();
        await foreach (var entry in log.ReplayAsync(session, afterMessageId: second.Id))
        {
            got.Add(entry.MessageId);
        }

        got.Should().Equal(third.Id);
    }

    [Fact]
    public async Task ReplayWithUnknownAnchorThrowsNotFound()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();
        await log.AppendAsync(session, MakePing());

        Func<Task> act = async () =>
        {
            await foreach (var entry in log.ReplayAsync(session, afterMessageId: MessageId.FromString("msg_unknown")))
            {
                _ = entry;
            }
        };

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task IdempotencyRecordReturnsOriginalOnRepeat()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();
        var key = IdempotencyKey.FromString("refund-ord_4812");
        var firstId = MessageId.New();
        var secondId = MessageId.New();

        var first = await log.RecordIdempotentAsync("nick@x", key, session, firstId);
        var second = await log.RecordIdempotentAsync("nick@x", key, session, secondId);

        first.Outcome.Should().Be(EventLogAppendResult.Appended);
        first.MessageId.Should().Be(firstId);
        second.Outcome.Should().Be(EventLogAppendResult.Duplicate);
        second.MessageId.Should().Be(firstId);
    }

    [Fact]
    public async Task IdempotencyKeyScopedByPrincipal()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();
        var key = IdempotencyKey.FromString("dup");
        var aId = MessageId.New();
        var bId = MessageId.New();

        var a = await log.RecordIdempotentAsync("alice", key, session, aId);
        var b = await log.RecordIdempotentAsync("bob", key, session, bId);

        a.Outcome.Should().Be(EventLogAppendResult.Appended);
        b.Outcome.Should().Be(EventLogAppendResult.Appended);
        b.MessageId.Should().Be(bId);
    }

    [Fact]
    public async Task SessionsAreIsolated()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var s1 = SessionId.New();
        var s2 = SessionId.New();
        await log.AppendAsync(s1, MakePing());
        await log.AppendAsync(s2, MakePing());

        (await log.CountAsync(s1)).Should().Be(1);
        (await log.CountAsync(s2)).Should().Be(1);
    }
}
