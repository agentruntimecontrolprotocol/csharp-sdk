// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Wire;

namespace Arcp.Core.Transport;

/// <summary>The transport abstraction for an ARCP peer. WebSocket, stdio, and in-memory all implement
/// this contract (spec §4).</summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Send one envelope to the peer.</summary>
    ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default);

    /// <summary>Async stream of envelopes received from the peer. Iteration ends when the transport
    /// is closed.</summary>
    IAsyncEnumerable<Envelope> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Close the transport. After this completes, send/receive raise.</summary>
    ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default);
}
