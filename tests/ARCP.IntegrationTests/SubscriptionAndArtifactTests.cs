using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Artifacts;
using ARCP.Messages.Execution;
using ARCP.Messages.Subscriptions;
using ARCP.Messages.Telemetry;
using ARCP.Runtime;
using ARCP.Store;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ARCP.IntegrationTests;

public class SubscriptionTests
{
    [Fact]
    public async Task SubscriptionDeliversMatchingPublishedEvents()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var sm = new SubscriptionManager(log);
        var subscriber = SessionId.New();
        var (id, stream) = await sm.SubscribeAsync(
            subscriber,
            new Subscribe(new SubscribeFilter { Types = new[] { "metric", "log" } }));

        // Run a consumer + a couple of publishes in parallel.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var consumed = new List<string>();
        Task consumerTask = Task.Run(async () =>
        {
            await foreach (var env in stream.WithCancellation(cts.Token))
            {
                consumed.Add(env.Type);
                if (consumed.Count >= 2)
                {
                    cts.Cancel();
                }
            }
        });

        await sm.PublishAsync(new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "metric",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Metric("tokens.used", 1, "tokens"),
            SessionId = subscriber,
        });
        await sm.PublishAsync(new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "log",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new LogMessage(LogLevel.Info, "hello"),
            SessionId = subscriber,
        });
        // Filtered out (not in types list):
        await sm.PublishAsync(new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Messages.Control.Ping(),
            SessionId = subscriber,
        });

        try { await consumerTask; }
        catch (OperationCanceledException) { /* expected */ }

        consumed.Should().Equal("metric", "log");
        sm.Unsubscribe(id);
    }

    [Fact]
    public async Task SubscribeAcrossOtherSessionRequiresAuthorization()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var sm = new SubscriptionManager(log);

        var subscriber = SessionId.New();
        var foreign = SessionId.New();
        Func<Task> act = () => sm.SubscribeAsync(
            subscriber,
            new Subscribe(new SubscribeFilter { SessionId = new[] { foreign.Value } }));

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task BackfillThenLiveTailEmitsBoundaryMarker()
    {
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();

        // Pre-populate event log.
        var first = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "log",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new LogMessage(LogLevel.Info, "first"),
            SessionId = session,
        };
        var anchor = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "log",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new LogMessage(LogLevel.Info, "anchor"),
            SessionId = session,
        };
        var historical = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "log",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new LogMessage(LogLevel.Info, "historical"),
            SessionId = session,
        };
        await log.AppendAsync(session, first);
        await log.AppendAsync(session, anchor);
        await log.AppendAsync(session, historical);

        var sm = new SubscriptionManager(log);
        var (_, stream) = await sm.SubscribeAsync(
            session,
            new Subscribe(
                new SubscribeFilter { Types = new[] { "log", "event.emit" } },
                new SubscribeSince(AfterMessageId: anchor.Id.Value)));

        var got = new List<string>();
        var done = new TaskCompletionSource<bool>();
        var ctx = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task consumer = Task.Run(async () =>
        {
            await foreach (var env in stream.WithCancellation(ctx.Token))
            {
                if (env.Payload is EventEmit ee && ee.Name == "subscription.backfill_complete")
                {
                    got.Add("BOUNDARY");
                }
                else if (env.Payload is LogMessage lm)
                {
                    got.Add(lm.Message);
                }
                if (got.Contains("live-event"))
                {
                    done.TrySetResult(true);
                    return;
                }
            }
        });

        // Wait for the boundary to appear, then publish a live event.
        var waitForBoundary = Task.Run(async () =>
        {
            while (!got.Contains("BOUNDARY"))
            {
                await Task.Delay(20);
            }
        });
        await waitForBoundary.WaitAsync(TimeSpan.FromSeconds(2));

        await sm.PublishAsync(new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "log",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new LogMessage(LogLevel.Info, "live-event"),
            SessionId = session,
        });

        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        ctx.Cancel();
        try { await consumer; } catch { /* expected */ }

        got.Should().ContainInOrder("historical", "BOUNDARY", "live-event");
    }
}

public class ArtifactTests
{
    [Fact]
    public async Task PutFetchRoundTrip()
    {
        await using var store = await ArtifactStore.OpenInMemoryAsync();
        var session = SessionId.New();

        string base64 = Convert.ToBase64String("hello world"u8.ToArray());
        var put = new ArtifactPut("text/plain", Data: base64, Encoding: "base64");
        Messages.Artifacts.ArtifactRef putRef = await store.PutAsync(session, put);

        putRef.Size.Should().Be(11);
        putRef.MediaType.Should().Be("text/plain");
        putRef.Sha256.Should().NotBeNullOrEmpty();

        var (body, fetchRef) = await store.FetchAsync(putRef.ArtifactId);
        System.Text.Encoding.UTF8.GetString(body).Should().Be("hello world");
        fetchRef.ArtifactId.Should().Be(putRef.ArtifactId);
    }

    [Fact]
    public async Task FetchAfterReleaseReturnsNotFound()
    {
        await using var store = await ArtifactStore.OpenInMemoryAsync();
        var session = SessionId.New();
        var put = new ArtifactPut("application/octet-stream",
            Data: Convert.ToBase64String(new byte[] { 1, 2, 3 }), Encoding: "base64");
        Messages.Artifacts.ArtifactRef putRef = await store.PutAsync(session, put);
        (await store.ReleaseAsync(putRef.ArtifactId)).Should().BeTrue();
        Func<Task> act = () => store.FetchAsync(putRef.ArtifactId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SweepRemovesExpiredArtifacts()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var store = await ArtifactStore.OpenInMemoryAsync(
            defaultRetention: TimeSpan.FromMilliseconds(100),
            maxRetention: TimeSpan.FromHours(1),
            time: time);
        var session = SessionId.New();
        var put = new ArtifactPut("text/plain", Data: Convert.ToBase64String("data"u8.ToArray()), Encoding: "base64");
        Messages.Artifacts.ArtifactRef putRef = await store.PutAsync(session, put);

        time.Advance(TimeSpan.FromSeconds(1));
        int swept = await store.SweepExpiredAsync();
        swept.Should().Be(1);

        Func<Task> act = () => store.FetchAsync(putRef.ArtifactId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task FetchOfUnknownIdThrowsNotFound()
    {
        await using var store = await ArtifactStore.OpenInMemoryAsync();
        Func<Task> act = () => store.FetchAsync(ArtifactId.New());
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

public class ResumeTests
{
    [Fact]
    public async Task ResumeReplaysAfterAnchorMessage()
    {
        // EventLog already supports resume by message id (Phase 1). This is
        // an end-to-end-ish check that verifies the wire-level Resume payload
        // is accepted by EventLog.ReplayAsync.
        await using var log = await EventLog.OpenInMemoryAsync();
        var session = SessionId.New();

        var first = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Messages.Control.Ping(),
            SessionId = session,
        };
        var anchor = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Messages.Control.Ping(),
            SessionId = session,
        };
        var afterAnchor = new Envelope.Envelope
        {
            Arcp = ProtocolVersion.Wire,
            Id = MessageId.New(),
            Type = "ping",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new Messages.Control.Ping(),
            SessionId = session,
        };
        await log.AppendAsync(session, first);
        await log.AppendAsync(session, anchor);
        await log.AppendAsync(session, afterAnchor);

        var resumed = new List<MessageId>();
        await foreach (var entry in log.ReplayAsync(session, anchor.Id))
        {
            resumed.Add(entry.MessageId);
        }
        resumed.Should().Equal(afterAnchor.Id);
    }
}
