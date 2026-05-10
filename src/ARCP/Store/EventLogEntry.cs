using ARCP.Envelope;
using ARCP.Ids;

namespace ARCP.Store;

/// <summary>
/// A persisted envelope row from <see cref="EventLog" />. Maintains the
/// monotonic <see cref="Sequence" /> assigned at append time so consumers can
/// replay in canonical order per RFC-0001-v2 §19.
/// </summary>
/// <param name="Sequence">Monotonic per-session sequence (ascending).</param>
/// <param name="Envelope">The persisted envelope (rehydrated through <see cref="EnvelopeJson" />).</param>
public readonly record struct EventLogEntry(long Sequence, Envelope.Envelope Envelope)
{
    /// <summary>Convenience: the envelope's <see cref="MessageId" />.</summary>
    public MessageId MessageId => Envelope.Id;
}

/// <summary>
/// Outcome of <see cref="EventLog.AppendAsync" />.
/// </summary>
public enum EventLogAppendResult
{
    /// <summary>The envelope was newly inserted.</summary>
    Appended,

    /// <summary>An envelope with the same <see cref="MessageId" /> already existed; this insert was a no-op.</summary>
    Duplicate,
}

/// <summary>
/// Outcome of <see cref="EventLog.RecordIdempotentAsync" />.
/// </summary>
/// <param name="Outcome">Whether the key was newly recorded or matched an existing record.</param>
/// <param name="MessageId">
/// The original <see cref="MessageId" /> recorded under this idempotency key; equal to
/// the input message id when <see cref="Outcome" /> is <see cref="EventLogAppendResult.Appended" />.
/// </param>
public readonly record struct IdempotencyOutcome(EventLogAppendResult Outcome, MessageId MessageId);
