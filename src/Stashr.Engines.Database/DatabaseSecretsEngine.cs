using System.Globalization;
using System.Security.Cryptography;
using Npgsql;
using Stashr.Core.Engines;
using Stashr.Core.Leases;
using Stashr.Core.Storage;
using Stashr.Engine.Leases;

namespace Stashr.Engines.Database;

/// <summary>A freshly issued dynamic database credential.</summary>
public sealed record DatabaseCredential
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string LeaseId { get; init; }
    public required long TtlSeconds { get; init; }
}

/// <summary>
/// Dynamic PostgreSQL secrets (ADR-0009/0006): on <c>creds/{role}</c> it creates a short-lived
/// login role on the fly and registers a lease; when the lease expires or is revoked it drops
/// the role. The admin connection and role templates come from <see cref="DatabaseEngineOptions"/>.
/// </summary>
public sealed class DatabaseSecretsEngine(LeaseManager leases, DatabaseEngineOptions options)
    : ISecretsEngine, ILeaseRevoker
{
    private const string DefaultCreation =
        "CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}';";

    private const string DefaultRevocation =
        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = '{{name}}'; " +
        "DROP ROLE IF EXISTS \"{{name}}\";";

    public string Type => "database";
    public string LeaseType => "database";

    public async Task<EngineResponse> HandleAsync(EngineRequest request, IStorageView storage, CancellationToken ct = default)
    {
        if (request.Operation == EngineOp.Read && request.Path.StartsWith("creds/", StringComparison.Ordinal))
        {
            var role = request.Path.Substring("creds/".Length);
            var cred = await GenerateAsync(role, ct);
            if (cred is null) return EngineResponse.Missing;
            return new EngineResponse
            {
                Data = new Dictionary<string, string>
                {
                    ["username"] = cred.Username,
                    ["password"] = cred.Password,
                    ["lease_id"] = cred.LeaseId,
                    ["lease_duration"] = cred.TtlSeconds.ToString(CultureInfo.InvariantCulture),
                },
            };
        }
        return EngineResponse.Missing;
    }

    /// <summary>Create a dynamic role for <paramref name="role"/> and a lease. Null if role unknown.</summary>
    public async Task<DatabaseCredential?> GenerateAsync(string role, CancellationToken ct = default)
    {
        if (!options.Roles.TryGetValue(role, out var def)) return null;

        var username = "stashr_" + RandomHex(8).ToLowerInvariant();
        var password = RandomHex(24);
        var ttl = TimeSpan.FromSeconds(def.DefaultTtlSeconds > 0 ? def.DefaultTtlSeconds : 3600);
        var expiration = DateTimeOffset.UtcNow.Add(ttl).ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var creation = string.IsNullOrWhiteSpace(def.CreationStatements) ? DefaultCreation : def.CreationStatements;
        var sql = creation
            .Replace("{{name}}", username)
            .Replace("{{password}}", password)
            .Replace("{{expiration}}", expiration);
        await ExecuteAsync(sql, ct);

        var lease = await leases.CreateAsync(
            LeaseType, new Dictionary<string, string> { ["username"] = username, ["role"] = role }, ttl, ct);

        return new DatabaseCredential
        {
            Username = username,
            Password = password,
            LeaseId = lease.LeaseId,
            TtlSeconds = (long)ttl.TotalSeconds,
        };
    }

    public async Task RevokeAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
    {
        if (!data.TryGetValue("username", out var username)) return;
        options.Roles.TryGetValue(data.TryGetValue("role", out var r) ? r : string.Empty, out var def);

        var revocation = def is null || string.IsNullOrWhiteSpace(def.RevocationStatements)
            ? DefaultRevocation
            : def.RevocationStatements;
        await ExecuteAsync(revocation.Replace("{{name}}", username), ct);
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(options.AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string RandomHex(int byteCount)
    {
        var b = new byte[byteCount];
        RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b);
    }
}
