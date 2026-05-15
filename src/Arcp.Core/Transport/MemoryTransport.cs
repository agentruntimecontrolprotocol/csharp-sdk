// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arcp.Core.Wire;

namespace Arcp.Core.Transport;

/// <summary>In-process transport pair for tests and the same-process worker. Mirrors TS's
/// <c>MemoryTransport.pair()</c> idiom from <c>@arcp/core</c>.</summary>
public sealed class MemoryTransport : ITransport
{
    private readonly Channel<Envelope> _outbound;
    private readonly Channel<Envelope> _inbound;
    private bool _closed;

    private MemoryTransport(Channel<Envelope> outbound, Channel<Envelope> inbound)
    {
        _outbound = outbound;
        _inbound = inbound;
    }

    public static (MemoryTransport Client, MemoryTransport Server) Pair()
    {
        var clientToServer = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var serverToClient = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var client = new MemoryTransport(outbound: clientToServer, inbound: serverToClient);
        var server = new MemoryTransport(outbound: serverToClient, inbound: clientToServer);
        return (client, server);
    }

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (_closed) throw new InvalidOperationException("Transport is closed.");
        return _outbound.Writer.WriteAsync(envelope, cancellationToken);
    }

    public async IAsyncEnumerable<Envelope> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_inbound.Reader.TryRead(out var env))
            {
                yield return env;
            }
        }
    }

    public ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_closed) return ValueTask.CompletedTask;
        _closed = true;
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_closed)
        {
            _closed = true;
            _outbound.Writer.TryComplete();
        }
        return ValueTask.CompletedTask;
    }
}
