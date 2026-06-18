using System.Runtime.CompilerServices;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Stashr.Core;
using Stashr.Core.Cryptography;
using Stashr.Core.Model;
using Stashr.Core.Storage;

namespace Stashr.Storage.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="ISecretStore"/> (ADR-0007). Persists ciphertext, wrapped keys,
/// metadata, policies, tokens and audit — never plaintext or the master key. Policies, tokens
/// and audit entries are stored as JSONB; secret/key ciphertext is stored as discrete columns.
/// </summary>
public sealed class PostgresSecretStore : ISecretStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _ds;

    public PostgresSecretStore(string connectionString)
        => _ds = NpgsqlDataSource.Create(connectionString);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(PostgresSchema.Ddl);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- key ring ---

    public async Task<WrappedKey?> GetActiveKeyAsync(CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT version, nonce, ciphertext, tag, algorithm, state, created_at FROM stashr_key_ring " +
            "WHERE state = 'active' ORDER BY version DESC LIMIT 1");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadKey(r) : null;
    }

    public async Task<WrappedKey?> GetKeyVersionAsync(int version, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT version, nonce, ciphertext, tag, algorithm, state, created_at FROM stashr_key_ring WHERE version = @v");
        cmd.Parameters.AddWithValue("v", version);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadKey(r) : null;
    }

    public async Task PutKeyAsync(WrappedKey key, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO stashr_key_ring (version, nonce, ciphertext, tag, algorithm, state, created_at) " +
            "VALUES (@v, @n, @c, @t, @a, @s, @ts) " +
            "ON CONFLICT (version) DO UPDATE SET state = EXCLUDED.state");
        cmd.Parameters.AddWithValue("v", key.Version);
        cmd.Parameters.AddWithValue("n", key.Wrapped.Nonce);
        cmd.Parameters.AddWithValue("c", key.Wrapped.Ciphertext);
        cmd.Parameters.AddWithValue("t", key.Wrapped.Tag);
        cmd.Parameters.AddWithValue("a", key.Wrapped.Algorithm);
        cmd.Parameters.AddWithValue("s", key.State);
        cmd.Parameters.AddWithValue("ts", key.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static WrappedKey ReadKey(NpgsqlDataReader r) => new()
    {
        Version = r.GetInt32(0),
        Wrapped = new SealedBlob
        {
            Nonce = r.GetFieldValue<byte[]>(1),
            Ciphertext = r.GetFieldValue<byte[]>(2),
            Tag = r.GetFieldValue<byte[]>(3),
            Algorithm = r.GetString(4),
        },
        State = r.GetString(5),
        CreatedAt = r.GetFieldValue<DateTimeOffset>(6),
    };

    // --- seal config ---

    public async Task<byte[]?> GetSealConfigAsync(CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT sealed_root FROM stashr_seal_config WHERE id = 1");
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as byte[];
    }

    public async Task PutSealConfigAsync(byte[] sealedRoot, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO stashr_seal_config (id, sealed_root) VALUES (1, @r) " +
            "ON CONFLICT (id) DO UPDATE SET sealed_root = EXCLUDED.sealed_root");
        cmd.Parameters.AddWithValue("r", sealedRoot);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- KV ---

    public async Task<SecretMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT path, current_version, max_versions, cas_required, created_at, updated_at " +
            "FROM stashr_secret_meta WHERE path = @p");
        cmd.Parameters.AddWithValue("p", path);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new SecretMetadata
        {
            Path = r.GetString(0),
            CurrentVersion = r.GetInt32(1),
            MaxVersions = r.GetInt32(2),
            CasRequired = r.GetBoolean(3),
            CreatedAt = r.GetFieldValue<DateTimeOffset>(4),
            UpdatedAt = r.GetFieldValue<DateTimeOffset>(5),
        };
    }

    public async Task<SecretRecord?> GetVersionAsync(string path, int? version, CancellationToken ct = default)
    {
        var sql = version is null
            ? "SELECT path, version, nonce, ciphertext, tag, algorithm, key_version, created_at, deleted_at, destroyed " +
              "FROM stashr_secret WHERE path = @p ORDER BY version DESC LIMIT 1"
            : "SELECT path, version, nonce, ciphertext, tag, algorithm, key_version, created_at, deleted_at, destroyed " +
              "FROM stashr_secret WHERE path = @p AND version = @v";
        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("p", path);
        if (version is { } v) cmd.Parameters.AddWithValue("v", v);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadSecret(r) : null;
    }

    private static SecretRecord ReadSecret(NpgsqlDataReader r) => new()
    {
        Path = r.GetString(0),
        Version = r.GetInt32(1),
        Ciphertext = new SealedBlob
        {
            Nonce = r.GetFieldValue<byte[]>(2),
            Ciphertext = r.GetFieldValue<byte[]>(3),
            Tag = r.GetFieldValue<byte[]>(4),
            Algorithm = r.GetString(5),
        },
        KeyVersion = r.GetInt32(6),
        CreatedAt = r.GetFieldValue<DateTimeOffset>(7),
        DeletedAt = r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        Destroyed = r.GetBoolean(9),
    };

    public async Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken ct = default)
    {
        var prefix = pathPrefix.Length == 0 || pathPrefix.EndsWith('/') ? pathPrefix : pathPrefix + "/";
        await using var cmd = _ds.CreateCommand("SELECT path FROM stashr_secret_meta WHERE path LIKE @like");
        cmd.Parameters.AddWithValue("like", prefix + "%");

        var children = new SortedSet<string>(StringComparer.Ordinal);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var full = r.GetString(0);
            var remainder = full.Substring(prefix.Length);
            var slash = remainder.IndexOf('/');
            children.Add(slash < 0 ? remainder : remainder.Substring(0, slash + 1));
        }
        return children.ToList();
    }

    public async Task<int> PutVersionAsync(SecretRecord record, int? expectedCurrentVersion, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var current = 0;
        var maxVersions = 10;
        var existed = false;
        await using (var sel = new NpgsqlCommand(
            "SELECT current_version, max_versions FROM stashr_secret_meta WHERE path = @p FOR UPDATE", conn, tx))
        {
            sel.Parameters.AddWithValue("p", record.Path);
            await using var r = await sel.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                existed = true;
                current = r.GetInt32(0);
                maxVersions = r.GetInt32(1);
            }
        }

        if (expectedCurrentVersion is { } cas && cas != current)
            throw new CasMismatchException(cas, current);

        var next = current + 1;

        await using (var ins = new NpgsqlCommand(
            "INSERT INTO stashr_secret (path, version, nonce, ciphertext, tag, algorithm, key_version, created_at, destroyed) " +
            "VALUES (@p, @v, @n, @c, @t, @a, @kv, @ts, FALSE)", conn, tx))
        {
            ins.Parameters.AddWithValue("p", record.Path);
            ins.Parameters.AddWithValue("v", next);
            ins.Parameters.AddWithValue("n", record.Ciphertext.Nonce);
            ins.Parameters.AddWithValue("c", record.Ciphertext.Ciphertext);
            ins.Parameters.AddWithValue("t", record.Ciphertext.Tag);
            ins.Parameters.AddWithValue("a", record.Ciphertext.Algorithm);
            ins.Parameters.AddWithValue("kv", record.KeyVersion);
            ins.Parameters.AddWithValue("ts", record.CreatedAt);
            await ins.ExecuteNonQueryAsync(ct);
        }

        if (existed)
        {
            await using var up = new NpgsqlCommand(
                "UPDATE stashr_secret_meta SET current_version = @v, updated_at = @u WHERE path = @p", conn, tx);
            up.Parameters.AddWithValue("v", next);
            up.Parameters.AddWithValue("u", record.CreatedAt);
            up.Parameters.AddWithValue("p", record.Path);
            await up.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var up = new NpgsqlCommand(
                "INSERT INTO stashr_secret_meta (path, current_version, created_at, updated_at) VALUES (@p, @v, @ts, @ts)", conn, tx);
            up.Parameters.AddWithValue("p", record.Path);
            up.Parameters.AddWithValue("v", next);
            up.Parameters.AddWithValue("ts", record.CreatedAt);
            await up.ExecuteNonQueryAsync(ct);
        }

        // Prune history beyond max_versions.
        var cut = next - maxVersions;
        if (cut > 0)
        {
            await using var pr = new NpgsqlCommand(
                "DELETE FROM stashr_secret WHERE path = @p AND version <= @cut", conn, tx);
            pr.Parameters.AddWithValue("p", record.Path);
            pr.Parameters.AddWithValue("cut", cut);
            await pr.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return next;
    }

    public Task SoftDeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => SetFlag(path, versions, "deleted_at = now()", ct);

    public Task UndeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => SetFlag(path, versions, "deleted_at = NULL", ct);

    public Task DestroyAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => SetFlag(path, versions, "destroyed = TRUE", ct);

    private async Task SetFlag(string path, IReadOnlyList<int> versions, string setClause, CancellationToken ct)
    {
        if (versions.Count == 0) return;
        await using var cmd = _ds.CreateCommand(
            $"UPDATE stashr_secret SET {setClause} WHERE path = @p AND version = ANY(@vs)");
        cmd.Parameters.AddWithValue("p", path);
        cmd.Parameters.AddWithValue("vs", versions.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- policies ---

    public async Task<Policy?> GetPolicyAsync(string name, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT policy_json FROM stashr_policy WHERE name = @n");
        cmd.Parameters.AddWithValue("n", name);
        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<Policy>(json, Json);
    }

    public async Task PutPolicyAsync(Policy policy, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO stashr_policy (name, policy_json, version) VALUES (@n, @j, @v) " +
            "ON CONFLICT (name) DO UPDATE SET policy_json = EXCLUDED.policy_json, version = EXCLUDED.version");
        cmd.Parameters.AddWithValue("n", policy.Name);
        cmd.Parameters.Add(new NpgsqlParameter("j", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(policy, Json) });
        cmd.Parameters.AddWithValue("v", policy.Version);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListPoliciesAsync(CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT name FROM stashr_policy ORDER BY name");
        var names = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) names.Add(r.GetString(0));
        return names;
    }

    // --- tokens ---

    public async Task<TokenInfo?> GetTokenByAccessorAsync(string accessor, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT token_json FROM stashr_token WHERE accessor = @a");
        cmd.Parameters.AddWithValue("a", accessor);
        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<TokenInfo>(json, Json);
    }

    public async Task PutTokenAsync(TokenInfo token, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO stashr_token (accessor, token_json, expires_at) VALUES (@a, @j, @e) " +
            "ON CONFLICT (accessor) DO UPDATE SET token_json = EXCLUDED.token_json, expires_at = EXCLUDED.expires_at");
        cmd.Parameters.AddWithValue("a", token.Accessor);
        cmd.Parameters.Add(new NpgsqlParameter("j", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(token, Json) });
        cmd.Parameters.AddWithValue("e", token.ExpiresAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteTokenAsync(string accessor, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("DELETE FROM stashr_token WHERE accessor = @a");
        cmd.Parameters.AddWithValue("a", accessor);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- audit ---

    public async Task AppendAuditAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO stashr_audit (seq, entry_json, chain_hash) VALUES (@s, @j, @h)");
        cmd.Parameters.AddWithValue("s", entry.Seq);
        cmd.Parameters.Add(new NpgsqlParameter("j", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(entry, Json) });
        cmd.Parameters.AddWithValue("h", (object?)entry.ChainHash ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> GetLastAuditSeqAsync(CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT COALESCE(MAX(seq), 0) FROM stashr_audit");
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : 0;
    }

    public async IAsyncEnumerable<AuditEntry> ReadAuditAsync(
        long fromSeq, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT entry_json FROM stashr_audit WHERE seq >= @s ORDER BY seq");
        cmd.Parameters.AddWithValue("s", fromSeq);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var entry = JsonSerializer.Deserialize<AuditEntry>(r.GetString(0), Json);
            if (entry is not null) yield return entry;
        }
    }

    // --- generic blob storage (ADR-0009) ---

    public async Task<byte[]?> GetBlobAsync(string key, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT value FROM stashr_blob WHERE key = @k");
        cmd.Parameters.AddWithValue("k", key);
        return await cmd.ExecuteScalarAsync(ct) as byte[];
    }

    public async Task PutBlobAsync(string key, byte[] value, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO stashr_blob (key, value) VALUES (@k, @v) " +
            "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value");
        cmd.Parameters.AddWithValue("k", key);
        cmd.Parameters.AddWithValue("v", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListBlobAsync(string keyPrefix, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT key FROM stashr_blob WHERE key LIKE @p ORDER BY key");
        cmd.Parameters.AddWithValue("p", keyPrefix + "%");
        var keys = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) keys.Add(r.GetString(0));
        return keys;
    }

    public async Task DeleteBlobAsync(string key, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("DELETE FROM stashr_blob WHERE key = @k");
        cmd.Parameters.AddWithValue("k", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => _ds.DisposeAsync();
}
