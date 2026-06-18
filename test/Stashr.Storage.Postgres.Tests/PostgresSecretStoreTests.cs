using Npgsql;
using Stashr.Core;
using Stashr.Core.Cryptography;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Storage.Postgres;
using Testcontainers.PostgreSql;
using Xunit;

namespace Stashr.Storage.Postgres.Tests;

/// <summary>
/// Runs <see cref="PostgresSecretStore"/> against a real PostgreSQL instance (Testcontainers).
/// Requires Docker. Validates the SQL the in-memory tests can't: the CAS transaction, jsonb
/// round-trips, array parameters, and timestamptz reads (ADR-0007).
/// </summary>
public class PostgresSecretStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private PostgresSecretStore _store = null!;
    private bool _usedContainer;

    public async Task InitializeAsync()
    {
        // Run against a local/existing Postgres if STASHR_TEST_POSTGRES is set (no Docker needed);
        // otherwise spin up a throwaway container via Testcontainers (needs Docker).
        var external = Environment.GetEnvironmentVariable("STASHR_TEST_POSTGRES");
        string connectionString;
        if (!string.IsNullOrWhiteSpace(external))
        {
            connectionString = external!;
        }
        else
        {
            await _pg.StartAsync();
            _usedContainer = true;
            connectionString = _pg.GetConnectionString();
        }

        _store = new PostgresSecretStore(connectionString);
        await _store.InitializeAsync();
        await TruncateAsync(connectionString); // deterministic on a persistent DB
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (_usedContainer) await _pg.DisposeAsync();
    }

    private static async Task TruncateAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE stashr_key_ring, stashr_seal_config, stashr_secret, stashr_secret_meta, " +
            "stashr_policy, stashr_token, stashr_audit, stashr_blob RESTART IDENTITY CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static SecretRecord Record(string path) => new()
    {
        Path = path,
        Version = 0,
        KeyVersion = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        Ciphertext = new SealedBlob { Nonce = new byte[12], Ciphertext = new byte[] { 1, 2, 3 }, Tag = new byte[16] },
    };

    [Fact]
    public async Task Key_ring_round_trips_and_reports_active()
    {
        await _store.PutKeyAsync(new WrappedKey
        {
            Version = 1,
            State = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            Wrapped = new SealedBlob { Nonce = new byte[12], Ciphertext = new byte[] { 9 }, Tag = new byte[16] },
        });

        var active = await _store.GetActiveKeyAsync();
        Assert.NotNull(active);
        Assert.Equal(1, active!.Version);
    }

    [Fact]
    public async Task Seal_config_round_trips()
    {
        await _store.PutSealConfigAsync(new byte[] { 1, 2, 3, 4 });
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await _store.GetSealConfigAsync());
    }

    [Fact]
    public async Task PutVersion_increments_and_cas_is_enforced()
    {
        var v1 = await _store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: null);
        var v2 = await _store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: 1);
        Assert.Equal(1, v1);
        Assert.Equal(2, v2);

        await Assert.ThrowsAsync<CasMismatchException>(
            () => _store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: 99));

        var meta = await _store.GetMetadataAsync("secret/app/db");
        Assert.Equal(2, meta!.CurrentVersion);
    }

    [Fact]
    public async Task GetVersion_returns_latest_and_specific()
    {
        await _store.PutVersionAsync(Record("secret/x"), null);
        await _store.PutVersionAsync(Record("secret/x"), null);

        Assert.Equal(2, (await _store.GetVersionAsync("secret/x", null))!.Version);
        Assert.Equal(1, (await _store.GetVersionAsync("secret/x", 1))!.Version);
    }

    [Fact]
    public async Task Delete_lifecycle_uses_array_parameter()
    {
        await _store.PutVersionAsync(Record("secret/y"), null);

        await _store.SoftDeleteAsync("secret/y", new[] { 1 });
        Assert.NotNull((await _store.GetVersionAsync("secret/y", 1))!.DeletedAt);

        await _store.UndeleteAsync("secret/y", new[] { 1 });
        Assert.Null((await _store.GetVersionAsync("secret/y", 1))!.DeletedAt);

        await _store.DestroyAsync("secret/y", new[] { 1 });
        Assert.True((await _store.GetVersionAsync("secret/y", 1))!.Destroyed);
    }

    [Fact]
    public async Task List_returns_immediate_children()
    {
        await _store.PutVersionAsync(Record("secret/app/db"), null);
        await _store.PutVersionAsync(Record("secret/app/cache"), null);

        var children = await _store.ListAsync("secret/app");
        Assert.Contains("db", children);
        Assert.Contains("cache", children);
    }

    [Fact]
    public async Task Policy_jsonb_round_trips()
    {
        var policy = new Policy
        {
            Name = "app-read",
            Rules = new[] { new PolicyRule { PathPattern = "secret/app/*", Capabilities = Capability.Read | Capability.List } },
        };
        await _store.PutPolicyAsync(policy);

        var loaded = await _store.GetPolicyAsync("app-read");
        Assert.NotNull(loaded);
        Assert.Equal("secret/app/*", loaded!.Rules[0].PathPattern);
        Assert.True(loaded.Rules[0].Capabilities.HasFlag(Capability.Read));
        Assert.Contains("app-read", await _store.ListPoliciesAsync());
    }

    [Fact]
    public async Task Token_jsonb_round_trips_and_deletes()
    {
        var token = new TokenInfo
        {
            Accessor = "acc-1",
            TokenHmac = new byte[] { 1, 2, 3 },
            Policies = new[] { "app-read" },
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        await _store.PutTokenAsync(token);

        var loaded = await _store.GetTokenByAccessorAsync("acc-1");
        Assert.Equal("acc-1", loaded!.Accessor);
        Assert.Contains("app-read", loaded.Policies);

        await _store.DeleteTokenAsync("acc-1");
        Assert.Null(await _store.GetTokenByAccessorAsync("acc-1"));
    }

    [Fact]
    public async Task Blob_storage_round_trips()
    {
        await _store.PutBlobAsync("mounts/transit/keys/k1", new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, await _store.GetBlobAsync("mounts/transit/keys/k1"));
        Assert.Contains("mounts/transit/keys/k1", await _store.ListBlobAsync("mounts/transit/"));

        await _store.DeleteBlobAsync("mounts/transit/keys/k1");
        Assert.Null(await _store.GetBlobAsync("mounts/transit/keys/k1"));
    }

    [Fact]
    public async Task Audit_appends_and_reads_back_in_order()
    {
        for (long i = 1; i <= 3; i++)
            await _store.AppendAuditAsync(new AuditEntry
            {
                Seq = i, RequestId = $"r{i}", Type = "request", Operation = "read", ChainHash = $"h{i}",
            });

        Assert.Equal(3, await _store.GetLastAuditSeqAsync());

        var seqs = new List<long>();
        await foreach (var e in _store.ReadAuditAsync(2)) seqs.Add(e.Seq);
        Assert.Equal(new long[] { 2, 3 }, seqs);
    }
}
