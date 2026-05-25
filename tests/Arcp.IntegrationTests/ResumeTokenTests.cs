// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Caps;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class ResumeTokenTests
{
    [Fact]
    public async Task Resume_token_is_minted_and_advertised_in_welcome()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        await using var c = await ArcpClient.ConnectAsync(clientT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        });
        c.ResumeToken.Should().NotBeNullOrEmpty();
        c.ResumeToken!.Should().StartWith("rt_");
    }

    [Fact]
    public async Task Resume_with_unknown_token_returns_session_error_and_does_not_welcome()
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        var (clientT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        // Manually craft a hello with an unknown resume token and assert no welcome arrives
        // within a short window. The server emits a session.error with RESUME_WINDOW_EXPIRED.
        var hello = new Arcp.Core.Wire.Envelope
        {
            Type = MessageTypeNames.SessionHello,
            Payload = new SessionHelloPayload
            {
                Client = new ClientInfo { Name = "t", Version = "1" },
                ResumeToken = "rt_unknown_token_value",
                Capabilities = new Capabilities
                {
                    Encodings = new[] { "json" },
                    Features = Array.Empty<string>(),
                },
            },
        };
        await clientT.SendAsync(hello);

        Arcp.Core.Wire.Envelope? error = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await foreach (var env in clientT.ReceiveAsync(cts.Token))
            {
                if (env.Type == MessageTypeNames.SessionError)
                {
                    error = env;
                    break;
                }
                if (env.Type == MessageTypeNames.SessionWelcome)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        error.Should().NotBeNull();
        ((SessionErrorPayload)error!.Payload!).Code.Should().Be(Arcp.Core.Errors.ErrorCode.ResumeWindowExpired);
    }

    [Fact]
    public async Task Session_resume_message_replays_buffered_events_after_drop()
    {
        // Spec §6.3: a `session.resume` envelope (not `session.hello`) with a valid token plus
        // last_event_seq triggers replay of the session's buffered events.
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
        });
        server.RegisterAgent("chatty", async (ctx, ct) =>
        {
            for (var i = 0; i < 3; i++) await ctx.LogAsync("info", $"e{i}", ct);
            return "done";
        });
        var (initial, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));

        string? resumeToken;
        await using (var c = await ArcpClient.ConnectAsync(initial, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
        }))
        {
            resumeToken = c.ResumeToken;
            await c.SubmitAsync("chatty");
            // Let the agent emit before we tear down.
            await Task.Delay(100);
        }

        resumeToken.Should().NotBeNullOrEmpty();

        // Reconnect with a fresh transport and present session.resume manually.
        var (resumeT, srv2) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv2));

        var resumeEnv = new Arcp.Core.Wire.Envelope
        {
            Type = MessageTypeNames.SessionResume,
            Payload = new SessionResumePayload { ResumeToken = resumeToken!, LastEventSeq = 0 },
        };
        await resumeT.SendAsync(resumeEnv);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sawReplay = false;
        try
        {
            await foreach (var env in resumeT.ReceiveAsync(cts.Token))
            {
                if (env.Type == MessageTypeNames.JobEvent) { sawReplay = true; break; }
                if (env.Type == MessageTypeNames.SessionError) break;
            }
        }
        catch (OperationCanceledException) { }

        sawReplay.Should().BeTrue(because: "session.resume MUST replay buffered events with seq > last_event_seq");
    }
}
