// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class ClientGapDetectionTests
{
    // Spec §8.3: a client that receives an event_seq which skips the expected successor SHOULD treat
    // the session as broken. This drives a minimal hand-rolled runtime that emits event_seq 1 then 3
    // (skipping 2) and asserts the client raises a detectable broken-session signal.
    [Fact]
    public async Task Client_detects_an_event_seq_gap()
    {
        var (clientT, srv) = MemoryTransport.Pair();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var server = Task.Run(async () =>
        {
            await foreach (var env in srv.ReceiveAsync(cts.Token))
            {
                if (env.Type != MessageTypeNames.SessionHello) continue;
                await srv.SendAsync(new Envelope
                {
                    Type = MessageTypeNames.SessionWelcome,
                    SessionId = "sess_gap",
                    Payload = new SessionWelcomePayload
                    {
                        Runtime = new RuntimeInfo { Name = "rt", Version = "1" },
                        ResumeToken = "rt_x",
                        Capabilities = new Capabilities { Encodings = new[] { "json" }, Features = Array.Empty<string>() },
                    },
                });

                await srv.SendAsync(JobEvent(1));
                await srv.SendAsync(JobEvent(3)); // gap: expected 2
                return;
            }
        }, cts.Token);

        var gap = new TaskCompletionSource<(long Expected, long Received)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ArcpClient(clientT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Features = Array.Empty<string>(),
        });
        client.EventSeqGapDetected += (expected, received) => gap.TrySetResult((expected, received));

        await client.ConnectAsync(cts.Token);

        var observed = await gap.Task.WaitAsync(TimeSpan.FromSeconds(5));
        observed.Expected.Should().Be(2);
        observed.Received.Should().Be(3);
        client.IsSessionBroken.Should().BeTrue();

        await client.DisposeAsync();
        await server;
    }

    private static Envelope JobEvent(long seq) => new()
    {
        Type = MessageTypeNames.JobEvent,
        SessionId = "sess_gap",
        JobId = "job_1",
        EventSeq = seq,
        Payload = new JobEventPayload
        {
            Kind = EventKinds.Log,
            Ts = DateTimeOffset.UtcNow,
            Body = ArcpJson.ToJsonElement(new { msg = "x" }),
        },
    };
}
