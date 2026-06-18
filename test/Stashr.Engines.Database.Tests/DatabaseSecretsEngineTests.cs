using Npgsql;
using Stashr.Engine.Leases;
using Stashr.Engines.Database;
using Stashr.Storage.InMemory;
using Testcontainers.PostgreSql;
using Xunit;

namespace Stashr.Engines.Database.Tests;

/// <summary>
/// Proves the dynamic-secrets lifecycle against real PostgreSQL: generate a role, connect with
/// it, revoke the lease, and confirm the credential no longer works (ADR-0006/0009).
/// </summary>
public class DatabaseSecretsEngineTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private DatabaseSecretsEngine NewEngine(out LeaseManager leases)
    {
        var options = new DatabaseEngineOptions
        {
            AdminConnectionString = _pg.GetConnectionString(),
            Roles =
            {
                ["readonly"] = new DatabaseRole
                {
                    CreationStatements = "CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD '{{password}}';",
                    DefaultTtlSeconds = 60,
                },
            },
        };
        leases = new LeaseManager(new InMemorySecretStore());
        var engine = new DatabaseSecretsEngine(leases, options);
        leases.RegisterRevoker(engine);
        return engine;
    }

    private NpgsqlConnection ConnectionAs(string user, string password)
    {
        var csb = new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
        {
            Username = user,
            Password = password,
            Pooling = false, // each attempt re-authenticates (don't reuse a pooled, pre-revocation connection)
        };
        return new NpgsqlConnection(csb.ConnectionString);
    }

    [Fact]
    public async Task Generated_credentials_work_then_stop_after_revocation()
    {
        var engine = NewEngine(out var leases);

        var cred = await engine.GenerateAsync("readonly");
        Assert.NotNull(cred);

        // The generated role can actually connect.
        await using (var ok = ConnectionAs(cred!.Username, cred.Password))
            await ok.OpenAsync();

        // Revoke the lease → the role is dropped.
        Assert.True(await leases.RevokeAsync(cred!.LeaseId));

        // The credential no longer works.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var bad = ConnectionAs(cred.Username, cred.Password);
            await bad.OpenAsync();
        });
    }

    [Fact]
    public async Task Unknown_role_returns_null()
    {
        var engine = NewEngine(out _);
        Assert.Null(await engine.GenerateAsync("does-not-exist"));
    }

    [Fact]
    public async Task Expired_lease_sweep_drops_the_role()
    {
        var options = new DatabaseEngineOptions
        {
            AdminConnectionString = _pg.GetConnectionString(),
            Roles =
            {
                ["short"] = new DatabaseRole
                {
                    CreationStatements = "CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD '{{password}}';",
                    DefaultTtlSeconds = 1,
                },
            },
        };
        var leases = new LeaseManager(new InMemorySecretStore());
        var engine = new DatabaseSecretsEngine(leases, options);
        leases.RegisterRevoker(engine);

        var cred = await engine.GenerateAsync("short");
        Assert.NotNull(cred);

        await Task.Delay(1500); // let the 1-second lease expire
        var swept = await leases.RevokeExpiredAsync();
        Assert.Equal(1, swept);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var bad = ConnectionAs(cred!.Username, cred.Password);
            await bad.OpenAsync();
        });
    }
}
