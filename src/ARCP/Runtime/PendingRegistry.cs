using System.Collections.Concurrent;
using ARCP.Errors;
using ARCP.Ids;

namespace ARCP.Runtime;

/// <summary>
/// Bidirectional correlation-id-keyed pending registry per PLAN.md §6
/// ("Pending registry via TaskCompletionSource&lt;T&gt;").
/// </summary>
/// <typeparam name="T">The expected response payload type.</typeparam>
public sealed class PendingRegistry<T>
{
    private readonly ConcurrentDictionary<MessageId, PendingEntry> _waiters = new();

    /// <summary>
    /// Register a waiter for a response keyed by <paramref name="id" />, with
    /// <paramref name="deadline" /> as a fallback. The returned task completes
    /// either with the resolved response (via <see cref="Resolve" />), or with
    /// <see cref="DeadlineExceededException" /> when the deadline elapses, or
    /// with <see cref="OperationCanceledException" /> when
    /// <paramref name="cancellationToken" /> fires.
    /// </summary>
    /// <param name="id">The command id whose response we await.</param>
    /// <param name="deadline">The absolute deadline.</param>
    /// <param name="time">Time provider.</param>
    /// <param name="cancellationToken">External cancellation token.</param>
    /// <returns>A task that completes with the resolved response.</returns>
    public Task<T> RegisterAsync(
        MessageId id,
        DateTimeOffset deadline,
        TimeProvider time,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(time);
        TimeSpan delay = deadline - time.GetUtcNow();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource deadlineCts = new();
        ITimer timer = time.CreateTimer(static state =>
        {
            ((CancellationTokenSource)state!).Cancel();
        }, deadlineCts, delay, Timeout.InfiniteTimeSpan);

        CancellationTokenRegistration externalReg = cancellationToken.Register(() =>
        {
            if (_waiters.TryRemove(id, out PendingEntry? entry))
            {
                entry.Tcs.TrySetCanceled(cancellationToken);
                entry.Dispose();
            }
        });

        CancellationTokenRegistration deadlineReg = deadlineCts.Token.Register(() =>
        {
            if (_waiters.TryRemove(id, out PendingEntry? entry))
            {
                entry.Tcs.TrySetException(new DeadlineExceededException(
                    $"Pending response for {id} timed out after {delay.TotalMilliseconds:F0}ms."));
                entry.Dispose();
            }
        });

        var newEntry = new PendingEntry(tcs, timer, deadlineCts, externalReg, deadlineReg);
        if (!_waiters.TryAdd(id, newEntry))
        {
            newEntry.Dispose();
            throw new InvalidArgumentException($"Pending response already registered for {id}.");
        }

        return tcs.Task;
    }

    /// <summary>
    /// Resolve the pending response for <paramref name="id" /> with
    /// <paramref name="value" />.
    /// </summary>
    /// <param name="id">The command id.</param>
    /// <param name="value">The resolved value.</param>
    /// <returns><see langword="true" /> if a waiter was resolved.</returns>
    public bool Resolve(MessageId id, T value)
    {
        if (!_waiters.TryRemove(id, out PendingEntry? entry))
        {
            return false;
        }
        bool resolved = entry.Tcs.TrySetResult(value);
        entry.Dispose();
        return resolved;
    }

    /// <summary>
    /// Reject the pending response for <paramref name="id" /> with
    /// <paramref name="error" />.
    /// </summary>
    /// <param name="id">The command id.</param>
    /// <param name="error">The error.</param>
    /// <returns><see langword="true" /> if a waiter was rejected.</returns>
    public bool Reject(MessageId id, Exception error)
    {
        if (!_waiters.TryRemove(id, out PendingEntry? entry))
        {
            return false;
        }
        bool rejected = entry.Tcs.TrySetException(error);
        entry.Dispose();
        return rejected;
    }

    /// <summary>The number of currently pending waiters.</summary>
    public int Count => _waiters.Count;

    private sealed class PendingEntry : IDisposable
    {
        public TaskCompletionSource<T> Tcs { get; }

        private readonly ITimer _timer;
        private readonly CancellationTokenSource _deadlineCts;
        private readonly CancellationTokenRegistration _externalReg;
        private readonly CancellationTokenRegistration _deadlineReg;
        private bool _disposed;

        public PendingEntry(
            TaskCompletionSource<T> tcs,
            ITimer timer,
            CancellationTokenSource deadlineCts,
            CancellationTokenRegistration externalReg,
            CancellationTokenRegistration deadlineReg)
        {
            Tcs = tcs;
            _timer = timer;
            _deadlineCts = deadlineCts;
            _externalReg = externalReg;
            _deadlineReg = deadlineReg;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _externalReg.Dispose();
            _deadlineReg.Dispose();
            _timer.Dispose();
            _deadlineCts.Dispose();
        }
    }
}
