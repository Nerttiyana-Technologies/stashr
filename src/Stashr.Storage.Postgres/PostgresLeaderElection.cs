using Npgsql;
using Stashr.Core.Ha;

namespace Stashr.Storage.Postgres;

/// <summary>
/// Advisory-lock leader election (ADR-0002). Holds a session-level
/// <c>pg_try_advisory_lock</c> on a dedicated connection: while that connection lives, this node
/// is leader and no other node can acquire the same lock. On acquisition it bumps a monotonic
/// fencing epoch in <c>stashr_ha_epoch</c>. Pooling is disabled so the lock-holding session is
/// never recycled underneath us.
/// </summary>
public sealed class PostgresLeaderElection : ILeaderElection, IAsyncDisposable
{
    private const long LockKey = 0x53_54_41_53_48; // "STASH"
    private readonly string _connectionString;
    private NpgsqlConnection? _conn;

    public PostgresLeaderElection(string connectionString)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        _connectionString = csb.ConnectionString;
    }

    public long Epoch { get; private set; }

    public async Task<bool> TryAcquireAsync(CancellationToken ct = default)
    {
        if (_conn is not null) return true; // already leader

        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using (var ensure = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS stashr_ha_epoch (id INT PRIMARY KEY, epoch BIGINT NOT NULL); " +
            "INSERT INTO stashr_ha_epoch (id, epoch) VALUES (1, 0) ON CONFLICT (id) DO NOTHING;", conn))
            await ensure.ExecuteNonQueryAsync(ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@k)", conn))
        {
            lockCmd.Parameters.AddWithValue("k", LockKey);
            var acquired = (bool)(await lockCmd.ExecuteScalarAsync(ct))!;
            if (!acquired)
            {
                await conn.DisposeAsync();
                return false;
            }
        }

        _conn = conn;
        await using var bump = new NpgsqlCommand(
            "UPDATE stashr_ha_epoch SET epoch = epoch + 1 WHERE id = 1 RETURNING epoch", _conn);
        Epoch = (long)(await bump.ExecuteScalarAsync(ct))!;
        return true;
    }

    public async Task<bool> IsHeldAsync(CancellationToken ct = default)
    {
        if (_conn is null) return false;
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT 1", _conn);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ReleaseAsync(CancellationToken ct = default)
    {
        if (_conn is null) return;
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", _conn);
            cmd.Parameters.AddWithValue("k", LockKey);
            await cmd.ExecuteScalarAsync(ct);
        }
        catch { /* connection may already be gone */ }

        await _conn.DisposeAsync();
        _conn = null;
    }

    public async ValueTask DisposeAsync() => await ReleaseAsync();
}
