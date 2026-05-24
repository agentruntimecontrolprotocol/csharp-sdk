// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Messages;
using Arcp.Core.Transport;
using Arcp.Core.Wire;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class StdioTransportTests
{
    [Fact]
    public async Task SendAsync_writes_one_line_per_envelope()
    {
        var input = new MemoryStream();
        var output = new MemoryStream();
        await using var t = new StdioTransport(input, output);
        await t.SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionPing,
            Payload = new SessionPingPayload { Nonce = "abc", SentAt = DateTimeOffset.UnixEpoch },
        });
        var written = System.Text.Encoding.UTF8.GetString(output.ToArray());
        written.TrimEnd().Should().NotContain("\n");
        written.Should().EndWith("\n");
    }

    [Fact]
    public async Task ReceiveAsync_yields_envelopes_for_newline_delimited_json()
    {
        var input = new MemoryStream();
        var output = new MemoryStream();
        // Pre-fill input with two complete envelopes.
        var env1 = new Envelope
        {
            Type = MessageTypeNames.SessionPing,
            Payload = new SessionPingPayload { Nonce = "n1", SentAt = DateTimeOffset.UnixEpoch },
        };
        var env2 = new Envelope
        {
            Type = MessageTypeNames.SessionPing,
            Payload = new SessionPingPayload { Nonce = "n2", SentAt = DateTimeOffset.UnixEpoch },
        };
        using (var w = new StreamWriter(input, new System.Text.UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = true })
        {
            await w.WriteLineAsync(ArcpJson.Serialize(env1));
            await w.WriteLineAsync(ArcpJson.Serialize(env2));
            await w.WriteLineAsync("not-valid-json");
        }
        input.Seek(0, SeekOrigin.Begin);

        await using var t = new StdioTransport(input, output);
        var received = new List<Envelope>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await foreach (var env in t.ReceiveAsync(cts.Token))
            {
                received.Add(env);
                if (received.Count >= 2) break;
            }
        }
        catch (OperationCanceledException) { }
        received.Should().HaveCount(2);
        ((SessionPingPayload)received[0].Payload!).Nonce.Should().Be("n1");
        ((SessionPingPayload)received[1].Payload!).Nonce.Should().Be("n2");
    }

    [Fact]
    public async Task SendAsync_after_close_throws()
    {
        var input = new MemoryStream();
        var output = new MemoryStream();
        var t = new StdioTransport(input, output);
        await t.CloseAsync();
        var act = async () => await t.SendAsync(new Envelope
        {
            Type = MessageTypeNames.SessionPing,
            Payload = new SessionPingPayload { Nonce = "x", SentAt = DateTimeOffset.UnixEpoch },
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
        await t.DisposeAsync();
    }
}
