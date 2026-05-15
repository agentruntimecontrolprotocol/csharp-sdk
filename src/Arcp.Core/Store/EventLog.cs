// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Wire;

namespace Arcp.Core.Store;

/// <summary>An in-memory ring buffer for session-scoped events used to replay on resume (spec §6.3)
/// and on <c>job.subscribe</c> with history (spec §7.6). Trim points come from <c>session.ack</c>
/// (spec §6.5) but are advisory — the resume window is authoritative.</summary>
public sealed class EventLog
{
    private readonly object _gate = new();
    private readonly List<Envelope> _events = new();
    private long _nextSeq = 1;
    private long _lastAckedSeq;

    public int Capacity { get; }

    public EventLog(int capacity = 4096)
    {
        Capacity = capacity > 0 ? capacity : 4096;
    }

    public long NextSeq => Interlocked.Read(ref _nextSeq);

    public long LastAckedSeq => Interlocked.Read(ref _lastAckedSeq);

    public long HighWatermark
    {
        get { lock (_gate) return _events.Count == 0 ? 0 : _events[^1].EventSeq ?? 0; }
    }

    /// <summary>Append an event envelope, assigning it the next session-scoped <c>event_seq</c>.</summary>
    public Envelope Append(Envelope envelope)
    {
        lock (_gate)
        {
            var seq = _nextSeq++;
            var stamped = envelope with { EventSeq = seq };
            _events.Add(stamped);
            if (_events.Count > Capacity)
            {
                // Trim oldest above ack-cap if buffer is full.
                var trim = _events.Count - Capacity;
                _events.RemoveRange(0, trim);
            }
            return stamped;
        }
    }

    /// <summary>Return all buffered events with <c>seq &gt; fromSeq</c> in order.</summary>
    public IReadOnlyList<Envelope> ReadFrom(long fromSeq)
    {
        lock (_gate)
        {
            return _events.Where(e => (e.EventSeq ?? 0) > fromSeq).ToArray();
        }
    }

    /// <summary>Advisory trim of events with seq ≤ <paramref name="upToSeq"/> per spec §6.5.</summary>
    public void Trim(long upToSeq)
    {
        lock (_gate)
        {
            Interlocked.Exchange(ref _lastAckedSeq, Math.Max(_lastAckedSeq, upToSeq));
            // Spec §6.5: MUST NOT free unacked events even past the time window unless memory
            // limits force eviction. We retain a conservative buffer of unacked content.
            var removeCount = 0;
            for (var i = 0; i < _events.Count; i++)
            {
                if ((_events[i].EventSeq ?? 0) > upToSeq) break;
                removeCount++;
            }
            if (removeCount > 0 && _events.Count - removeCount >= 0)
            {
                _events.RemoveRange(0, removeCount);
            }
        }
    }
}
