using System.Reflection;
using System.Security.Cryptography;
using ARCP.Errors;
using ARCP.Ids;
using ARCP.Messages.Artifacts;
using Microsoft.Data.Sqlite;

namespace ARCP.Runtime;

/// <summary>
/// In-memory + SQLite-backed artifact store per RFC-0001-v2 §16. v0.1
/// supports inline base64 only — sidecar binary frames are deferred. A
/// periodic retention sweep evicts artifacts past their <c>expires_at</c>.
/// </summary>
public sealed class ArtifactStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TimeProvider _time;
    private readonly TimeSpan _defaultRetention;
    private readonly TimeSpan _maxRetention;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PeriodicTimer _sweepTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _sweepLoop;
    private bool _disposed;

    private ArtifactStore(SqliteConnection connection, TimeProvider time, TimeSpan defaultRetention, TimeSpan maxRetention, TimeSpan sweepInterval)
    {
        _connection = connection;
        _time = time;
        _defaultRetention = defaultRetention;
        _maxRetention = maxRetention;
        _sweepTimer = new PeriodicTimer(sweepInterval);
        _sweepLoop = Task.Run(SweepLoopAsync);
    }

    /// <summary>
    /// Open an artifact store backed by an existing SQLite connection (e.g.
    /// the same one the <see cref="Store.EventLog" /> uses).
    /// </summary>
    /// <param name="connection">An open <see cref="SqliteConnection" /> with the artifact schema applied.</param>
    /// <param name="defaultRetention">Default retention for newly-stored artifacts.</param>
    /// <param name="maxRetention">Hard upper bound on retention.</param>
    /// <param name="time">Time provider.</param>
    /// <param name="sweepInterval">How often to run the retention sweep.</param>
    /// <returns>A new <see cref="ArtifactStore" />.</returns>
    public static ArtifactStore CreateOnConnection(
        SqliteConnection connection,
        TimeSpan? defaultRetention = null,
        TimeSpan? maxRetention = null,
        TimeProvider? time = null,
        TimeSpan? sweepInterval = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new ArtifactStore(
            connection,
            time ?? TimeProvider.System,
            defaultRetention ?? TimeSpan.FromHours(24),
            maxRetention ?? TimeSpan.FromDays(7),
            sweepInterval ?? TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Open a fresh in-memory artifact store. Suitable for tests.
    /// </summary>
    /// <param name="defaultRetention">Default retention.</param>
    /// <param name="maxRetention">Maximum retention.</param>
    /// <param name="time">Time provider.</param>
    /// <param name="sweepInterval">Retention sweep interval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open artifact store.</returns>
    public static async Task<ArtifactStore> OpenInMemoryAsync(
        TimeSpan? defaultRetention = null,
        TimeSpan? maxRetention = null,
        TimeProvider? time = null,
        TimeSpan? sweepInterval = null,
        CancellationToken cancellationToken = default)
    {
        SqliteConnection conn = new("Data Source=:memory:;Cache=Shared");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = LoadSchema();
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return CreateOnConnection(conn, defaultRetention, maxRetention, time, sweepInterval);
    }

    private static string LoadSchema()
    {
        Assembly assembly = typeof(ArtifactStore).Assembly;
        string resourceName = "ARCP.Resources.Schema.sql";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InternalException($"Embedded SQL schema \"{resourceName}\" not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Persist an artifact and return its canonical reference.
    /// </summary>
    /// <param name="sessionId">Owning session id.</param>
    /// <param name="put">The artifact.put payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The artifact reference.</returns>
    public async Task<ArtifactRef> PutAsync(
        Ids.SessionId sessionId,
        ArtifactPut put,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(put);
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(put.Data))
        {
            throw new InvalidArgumentException("ArtifactPut.data is required (sidecar frames deferred to v0.2).");
        }

        byte[] body;
        try
        {
            body = Convert.FromBase64String(put.Data);
        }
        catch (FormatException ex)
        {
            throw new InvalidArgumentException("ArtifactPut.data must be valid base64.", ex);
        }

        ArtifactId id = put.ArtifactId ?? ArtifactId.New();
        TimeSpan ttl = put.TtlSeconds is { } secs
            ? TimeSpan.FromSeconds(secs)
            : _defaultRetention;
        if (ttl > _maxRetention)
        {
            ttl = _maxRetention;
        }
        DateTimeOffset expiresAt = _time.GetUtcNow() + ttl;
        string sha256 = ComputeSha256(body);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO artifacts (artifact_id, session_id, media_type, size, sha256, expires_at, body, created_at)
                VALUES ($id, $session, $mt, $size, $sha, $expires, $body, $created)
                ON CONFLICT(artifact_id) DO UPDATE SET
                    expires_at = excluded.expires_at;
                """;
            cmd.Parameters.AddWithValue("$id", id.Value);
            cmd.Parameters.AddWithValue("$session", sessionId.Value);
            cmd.Parameters.AddWithValue("$mt", put.MediaType);
            cmd.Parameters.AddWithValue("$size", body.LongLength);
            cmd.Parameters.AddWithValue("$sha", sha256);
            cmd.Parameters.AddWithValue("$expires", expiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$body", body);
            cmd.Parameters.AddWithValue("$created", _time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return new ArtifactRef(
            id,
            $"arcp://session/{sessionId}/artifact/{id}",
            put.MediaType,
            body.LongLength,
            sha256,
            expiresAt);
    }

    /// <summary>
    /// Fetch the body of an artifact by id.
    /// </summary>
    /// <param name="artifactId">The id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The body bytes and the canonical reference.</returns>
    /// <exception cref="NotFoundException">If the artifact does not exist or has expired.</exception>
    public async Task<(byte[] Body, ArtifactRef Ref)> FetchAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT session_id, media_type, size, sha256, expires_at, body FROM artifacts WHERE artifact_id = $id";
        cmd.Parameters.AddWithValue("$id", artifactId.Value);
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException($"Artifact {artifactId} not found.");
        }

        string session = reader.GetString(0);
        string mt = reader.GetString(1);
        long size = reader.GetInt64(2);
        string? sha = reader.IsDBNull(3) ? null : reader.GetString(3);
        DateTimeOffset? expiresAt = reader.IsDBNull(4)
            ? null
            : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        byte[] body = (byte[])reader.GetValue(5);

        if (expiresAt is { } e && e <= _time.GetUtcNow())
        {
            throw new NotFoundException($"Artifact {artifactId} has expired.");
        }

        var artifactRef = new ArtifactRef(
            artifactId,
            $"arcp://session/{session}/artifact/{artifactId}",
            mt,
            size,
            sha,
            expiresAt);
        return (body, artifactRef);
    }

    /// <summary>Release (delete) an artifact.</summary>
    /// <param name="artifactId">The id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a row was deleted.</returns>
    public async Task<bool> ReleaseAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM artifacts WHERE artifact_id = $id";
            cmd.Parameters.AddWithValue("$id", artifactId.Value);
            int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Delete every artifact whose <c>expires_at</c> is in the past. Returns
    /// the number of rows removed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of artifacts swept.</returns>
    public async Task<int> SweepExpiredAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM artifacts WHERE expires_at IS NOT NULL AND expires_at <= $now";
            cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SweepLoopAsync()
    {
        try
        {
            while (await _sweepTimer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                try
                {
                    await SweepExpiredAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // best effort
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private static string ComputeSha256(byte[] body) =>
        Convert.ToHexStringLower(SHA256.HashData(body));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _sweepLoop.ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
        _gate.Dispose();
        _shutdown.Dispose();
    }
}
