// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Client;
using Arcp.Core.Auth;
using Arcp.Core.Caps;
using Arcp.Core.Errors;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using Arcp.Runtime;
using FluentAssertions;
using Xunit;

namespace Arcp.IntegrationTests;

public class CancellationAckTests
{
    private static (ArcpServer server, MemoryTransport clientT) StartServer(Action<ArcpServer> configure, IBearerVerifier? auth = null)
    {
        var server = new ArcpServer(new ArcpServerOptions
        {
            Runtime = new RuntimeInfo { Name = "test-runtime", Version = "1.0.0" },
            Auth = auth,
        });
        configure(server);
        var (client, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        return (server, client);
    }

    private static Envelope Hello() => new()
    {
        Type = MessageTypeNames.SessionHello,
        Payload = new SessionHelloPayload
        {
            Client = new ClientInfo { Name = "t", Version = "1" },
            Capabilities = new Capabilities { Encodings = new[] { "json" }, Features = Array.Empty<string>() },
        },
    };

    [Fact]
    public async Task Successful_cancel_acks_with_job_cancelled_then_errors_with_CANCELLED()
    {
        // Spec §7.4: the runtime acknowledges with job.cancelled AND emits job.error{CANCELLED}.
        var (_, t) = StartServer(s => s.RegisterAgent("sleeper", async (ctx, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        }));

        await t.SendAsync(Hello());

        string? sessionId = null;
        string? jobId = null;
        Envelope? cancelledEnv = null;
        Envelope? errorEnv = null;
        var sawCancelledBeforeError = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var env in t.ReceiveAsync(cts.Token))
        {
            switch (env.Type)
            {
                case MessageTypeNames.SessionWelcome:
                    sessionId = env.SessionId;
                    await t.SendAsync(new Envelope
                    {
                        Type = MessageTypeNames.JobSubmit,
                        SessionId = sessionId,
                        Payload = new JobSubmitPayload { Agent = "sleeper" },
                    });
                    break;
                case MessageTypeNames.JobAccepted:
                    jobId = env.JobId;
                    await t.SendAsync(new Envelope
                    {
                        Type = MessageTypeNames.JobCancel,
                        SessionId = sessionId,
                        JobId = jobId,
                        Payload = new JobCancelPayload { JobId = jobId!, Reason = "stop" },
                    });
                    break;
                case MessageTypeNames.JobCancelled:
                    cancelledEnv = env;
                    break;
                case MessageTypeNames.JobError:
                    errorEnv = env;
                    sawCancelledBeforeError = cancelledEnv is not null;
                    break;
            }
            if (cancelledEnv is not null && errorEnv is not null) break;
        }

        cancelledEnv.Should().NotBeNull();
        ((JobCancelledPayload)cancelledEnv!.Payload!).JobId.Should().Be(jobId);
        sawCancelledBeforeError.Should().BeTrue("the cancel ack must precede the terminal job.error");
        errorEnv.Should().NotBeNull();
        var err = (JobErrorPayload)errorEnv!.Payload!;
        err.Code.Should().Be(ErrorCode.Cancelled);
        err.FinalStatus.Should().Be("cancelled");
    }

    [Fact]
    public async Task Cancel_of_unknown_job_yields_JOB_NOT_FOUND()
    {
        // Spec §12: cancelling a job the runtime does not know about surfaces JOB_NOT_FOUND
        // rather than being silently dropped.
        var (_, t) = StartServer(s => s.RegisterAgent("noop", (ctx, ct) => Task.FromResult<object?>(null)));
        await t.SendAsync(Hello());

        var unknownJobId = Arcp.Core.Ids.JobId.New().Value;
        Envelope? errEnv = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var env in t.ReceiveAsync(cts.Token))
        {
            if (env.Type == MessageTypeNames.SessionWelcome)
            {
                await t.SendAsync(new Envelope
                {
                    Type = MessageTypeNames.JobCancel,
                    SessionId = env.SessionId,
                    JobId = unknownJobId,
                    Payload = new JobCancelPayload { JobId = unknownJobId, Reason = "x" },
                });
            }
            else if (env.Type == MessageTypeNames.SessionError)
            {
                errEnv = env;
                break;
            }
        }

        errEnv.Should().NotBeNull();
        ((SessionErrorPayload)errEnv!.Payload!).Code.Should().Be(ErrorCode.JobNotFound);
    }

    [Fact]
    public async Task Second_session_of_same_principal_cannot_cancel_anothers_job()
    {
        // Spec §7.6/§14: cancellation is reserved for the submitting *session*, not merely the
        // principal. A second session of the same principal MUST NOT be able to cancel.
        var (server, ownerT) = StartServer(s => s.RegisterAgent("longish", async (ctx, ct) =>
        {
            await Task.Delay(500, ct);
            return "ok";
        }), new AllowAnyBearerVerifier());

        await using var owner = await ArcpClient.ConnectAsync(ownerT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "owner", Version = "1" },
            Token = "principal-alice",
        });
        var handle = await owner.SubmitAsync("longish");

        var (otherT, srv) = MemoryTransport.Pair();
        _ = Task.Run(() => server.AcceptAsync(srv));
        await using var other = await ArcpClient.ConnectAsync(otherT, new ArcpClientOptions
        {
            Client = new ClientInfo { Name = "other", Version = "1" },
            Token = "principal-alice", // SAME principal, DIFFERENT session.
        });

        await otherT.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobCancel,
            SessionId = other.SessionId.Value,
            JobId = handle.JobId.Value,
            Payload = new JobCancelPayload { JobId = handle.JobId.Value, Reason = "nope" },
        });

        // The cancel is denied (session-scoped authority) → the owner's job completes successfully.
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(3));
        result.Success.Should().BeTrue();
    }
}
