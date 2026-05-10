using System.Reflection;
using System.Text.Json;
using ARCP.Envelope;
using ARCP.Errors;
using ARCP.Ids;
using Microsoft.Data.Sqlite;

namespace ARCP.Store;

/// <summary>
/// Append-only SQLite-backed event log per RFC-0001-v2 §6.4 / §19.
/// </summary>
/// <remarks>
/// <para>
/// The log persists every envelope keyed by <c>(SessionId, MessageId)</c>;
/// duplicate inserts (transport replay) are silently absorbed via
/// <see cref="EventLogAppendResult.Duplicate" />. A separate idempotency
/// table maps <c>(principal, idempotency_key)</c> to the original
/// <see cref="MessageId" /> so retried logical commands return the previous
/// outcome.
/// </para>
/// <para>
/// All async APIs accept a <see cref="CancellationToken" /> as the last
/// parameter and pass it through to <c>Microsoft.Data.Sqlite</c>.
/// </para>
/// </remarks>
public sealed class EventLog : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JsonSerializerOptions _options;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    private EventLog(SqliteConnection connection, JsonSerializerOptions options)
    {
        _connection = connection;
        _options = options;
    }

    /// <summary>
    /// Open an in-memory <see cref="EventLog" /> with the schema applied.
    /// Suitable for tests and ephemeral runtimes.
    /// </summary>
    /// <param name="options">Serializer options; if <see langword="null" />, <see cref="EnvelopeJson.Options" /> is used.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open <see cref="EventLog" />.</returns>
    public static Task<EventLog> OpenInMemoryAsync(
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
        => OpenAsync("Data Source=:memory:;Cache=Shared", options, cancellationToken);

    /// <summary>
    /// Open a file-backed <see cref="EventLog" /> at <paramref name="path" />,
    /// creating it (and applying the schema) if necessary.
    /// </summary>
    /// <param name="path">Filesystem path to the SQLite database.</param>
    /// <param name="options">Serializer options; if <see langword="null" />, <see cref="EnvelopeJson.Options" /> is used.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open <see cref="EventLog" />.</returns>
    public static Task<EventLog> OpenFileAsync(
        string path,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenAsync($"Data Source={path}", options, cancellationToken);
    }

    private static async Task<EventLog> OpenAsync(
        string connectionString,
        JsonSerializerOptions? options,
        CancellationToken cancellationToken)
    {
        SqliteConnection conn = new(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = LoadSchema();
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return new EventLog(conn, options ?? EnvelopeJson.Options);
    }

    private static string LoadSchema()
    {
        Assembly assembly = typeof(EventLog).Assembly;
        string resourceName = "ARCP.Resources.Schema.sql";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InternalException(
                $"Embedded SQL schema resource \"{resourceName}\" not found in {assembly.FullName}.");
        }
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Append <paramref name="envelope" /> to the log under
    /// <paramref name="sessionId" />. Idempotent on
    /// <c>(SessionId, MessageId)</c>: a retried envelope with the same id
    /// returns <see cref="EventLogAppendResult.Duplicate" /> without
    /// re-inserting.
    /// </summary>
    /// <param name="sessionId">The session id this envelope belongs to.</param>
    /// <param name="envelope">The envelope to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the row was newly inserted or already present.</returns>
    public async Task<EventLogAppendResult> AppendAsync(
        SessionId sessionId,
        Envelope.Envelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ThrowIfDisposed();

        string body = JsonSerializer.Serialize(envelope, _options);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO envelopes
                    (session_id, message_id, type, job_id, stream_id, subscription_id,
                     trace_id, correlation_id, causation_id, priority, timestamp, sequence, body)
                VALUES
                    ($session_id, $message_id, $type, $job_id, $stream_id, $subscription_id,
                     $trace_id, $correlation_id, $causation_id, $priority, $timestamp,
                     COALESCE((SELECT MAX(sequence) + 1 FROM envelopes WHERE session_id = $session_id), 0),
                     $body)
                ON CONFLICT(session_id, message_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$session_id", sessionId.Value);
            cmd.Parameters.AddWithValue("$message_id", envelope.Id.Value);
            cmd.Parameters.AddWithValue("$type", envelope.Type);
            cmd.Parameters.AddWithValue("$job_id", (object?)envelope.JobId?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$stream_id", (object?)envelope.StreamId?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$subscription_id", (object?)envelope.SubscriptionId?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$trace_id", (object?)envelope.TraceId?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$correlation_id", (object?)envelope.CorrelationId?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$causation_id", (object?)envelope.CausationId?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$priority", (object?)envelope.Priority?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$timestamp", envelope.Timestamp.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$body", body);

            int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows == 1 ? EventLogAppendResult.Appended : EventLogAppendResult.Duplicate;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Replay every envelope previously appended under <paramref name="sessionId" />,
    /// optionally starting after a specific message id, in canonical sequence order.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="afterMessageId">If set, only envelopes whose sequence is strictly after this message's sequence are returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async iterator of <see cref="EventLogEntry" /> in sequence order.</returns>
    public async IAsyncEnumerable<EventLogEntry> ReplayAsync(
        SessionId sessionId,
        MessageId? afterMessageId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        long? after = null;
        if (afterMessageId is { } anchor)
        {
            await using SqliteCommand lookup = _connection.CreateCommand();
            lookup.CommandText = "SELECT sequence FROM envelopes WHERE session_id = $session_id AND message_id = $message_id";
            lookup.Parameters.AddWithValue("$session_id", sessionId.Value);
            lookup.Parameters.AddWithValue("$message_id", anchor.Value);
            object? value = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null || value is DBNull)
            {
                throw new NotFoundException(
                    $"Resume anchor {anchor} not found in session {sessionId}.");
            }
            after = (long)value;
        }

        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = after is null
            ? "SELECT sequence, body FROM envelopes WHERE session_id = $session_id ORDER BY sequence ASC"
            : "SELECT sequence, body FROM envelopes WHERE session_id = $session_id AND sequence > $after ORDER BY sequence ASC";
        cmd.Parameters.AddWithValue("$session_id", sessionId.Value);
        if (after is { } a)
        {
            cmd.Parameters.AddWithValue("$after", a);
        }

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long seq = reader.GetInt64(0);
            string body = reader.GetString(1);
            Envelope.Envelope? envelope = JsonSerializer.Deserialize<Envelope.Envelope>(body, _options);
            if (envelope is null)
            {
                throw new DataLossException(
                    $"Stored envelope at session={sessionId} seq={seq} deserialized to null.");
            }
            yield return new EventLogEntry(seq, envelope);
        }
    }

    /// <summary>
    /// Record a logical idempotency key. Returns the original
    /// <see cref="MessageId" /> if the key was previously recorded; otherwise
    /// inserts and returns <paramref name="messageId" />.
    /// </summary>
    /// <param name="principal">The authenticated principal (e.g. JWT subject, bearer-token user, or <c>"anonymous"</c>).</param>
    /// <param name="key">The user-supplied idempotency key.</param>
    /// <param name="sessionId">Session id of the new attempt.</param>
    /// <param name="messageId">Message id of the new attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome and the canonical original message id.</returns>
    public async Task<IdempotencyOutcome> RecordIdempotentAsync(
        string principal,
        IdempotencyKey key,
        SessionId sessionId,
        MessageId messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(principal);
        ThrowIfDisposed();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand insert = _connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO idempotency (principal, idempotency_key, session_id, message_id, created_at)
                VALUES ($principal, $key, $session_id, $message_id, $created_at)
                ON CONFLICT(principal, idempotency_key) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$principal", principal);
            insert.Parameters.AddWithValue("$key", key.Value);
            insert.Parameters.AddWithValue("$session_id", sessionId.Value);
            insert.Parameters.AddWithValue("$message_id", messageId.Value);
            insert.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

            int rows = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 1)
            {
                return new IdempotencyOutcome(EventLogAppendResult.Appended, messageId);
            }

            await using SqliteCommand select = _connection.CreateCommand();
            select.CommandText = "SELECT message_id FROM idempotency WHERE principal = $principal AND idempotency_key = $key";
            select.Parameters.AddWithValue("$principal", principal);
            select.Parameters.AddWithValue("$key", key.Value);
            object? raw = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            string? originalId = raw as string;
            if (string.IsNullOrEmpty(originalId))
            {
                throw new InternalException(
                    $"Idempotency lookup for ({principal}, {key}) returned no row after conflict.");
            }
            return new IdempotencyOutcome(EventLogAppendResult.Duplicate, MessageId.FromString(originalId));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Returns the count of envelopes persisted for <paramref name="sessionId" />.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count.</returns>
    public async Task<long> CountAsync(
        SessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM envelopes WHERE session_id = $session_id";
        cmd.Parameters.AddWithValue("$session_id", sessionId.Value);
        object? value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long l ? l : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Throws if this log has been disposed.</summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _writeGate.Dispose();
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
