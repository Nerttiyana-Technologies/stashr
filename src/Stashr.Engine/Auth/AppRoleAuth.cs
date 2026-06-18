using System.Text;
using System.Text.Json;
using Stashr.Core.Cryptography;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Engine.Identity;
using Stashr.Engine.Seal;
using Stashr.Engine.Tokens;

namespace Stashr.Engine.Auth;

/// <summary>
/// AppRole machine authentication (ADR-0006): a role has a semi-public <c>role_id</c> and is
/// presented alongside a secret <c>secret_id</c> to obtain a token carrying the role's policies.
/// Roles and secret-ids are stored as blobs; secret-ids are stored only as an HMAC, never in the
/// clear. Requires the engine to be unsealed (the secret-id HMAC key is DEK-derived).
/// </summary>
public sealed class AppRoleAuth(
    ISecretStore store, KeyManager keys, TokenManager tokens, ICryptoProvider crypto, IdentityStore? identity = null)
{
    public async Task<string> CreateRoleAsync(
        string name, IReadOnlyList<string> policies, TimeSpan tokenTtl, TimeSpan secretIdTtl, int secretIdNumUses,
        CancellationToken ct = default)
    {
        var existing = await ReadRoleAsync(name, ct);
        var roleId = existing?.RoleId ?? RandomHex(16);

        var role = new RoleData
        {
            RoleId = roleId,
            Policies = policies,
            TokenTtlSeconds = (long)tokenTtl.TotalSeconds,
            SecretIdTtlSeconds = (long)secretIdTtl.TotalSeconds,
            SecretIdNumUses = secretIdNumUses,
        };
        await store.PutBlobAsync(RoleKey(name), JsonSerializer.SerializeToUtf8Bytes(role), ct);
        await store.PutBlobAsync(RoleIdKey(roleId), Encoding.UTF8.GetBytes(name), ct);
        return roleId;
    }

    public async Task<string?> GetRoleIdAsync(string name, CancellationToken ct = default)
        => (await ReadRoleAsync(name, ct))?.RoleId;

    public async Task<string?> GenerateSecretIdAsync(string name, CancellationToken ct = default)
    {
        var role = await ReadRoleAsync(name, ct);
        if (role is null) return null;

        var secretId = RandomHex(32);
        var entry = new SecretIdData
        {
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = role.SecretIdTtlSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(role.SecretIdTtlSeconds) : null,
            RemainingUses = role.SecretIdNumUses,
        };
        await store.PutBlobAsync(SecretIdKey(name, secretId), JsonSerializer.SerializeToUtf8Bytes(entry), ct);
        return secretId;
    }

    public async Task<(string Token, TokenInfo Info)?> LoginAsync(string roleId, string secretId, CancellationToken ct = default)
    {
        var nameBytes = await store.GetBlobAsync(RoleIdKey(roleId), ct);
        if (nameBytes is null) return null;
        var name = Encoding.UTF8.GetString(nameBytes);

        var role = await ReadRoleAsync(name, ct);
        if (role is null) return null;

        var key = SecretIdKey(name, secretId);
        var entryBytes = await store.GetBlobAsync(key, ct);
        if (entryBytes is null) return null;
        var entry = JsonSerializer.Deserialize<SecretIdData>(entryBytes)!;

        if (entry.ExpiresAt is { } exp && DateTimeOffset.UtcNow > exp)
        {
            await store.DeleteBlobAsync(key, ct);
            return null;
        }

        if (entry.RemainingUses > 0)
        {
            var remaining = entry.RemainingUses - 1;
            if (remaining <= 0) await store.DeleteBlobAsync(key, ct);
            else await store.PutBlobAsync(key, JsonSerializer.SerializeToUtf8Bytes(entry with { RemainingUses = remaining }), ct);
        }

        var ttl = role.TokenTtlSeconds > 0 ? TimeSpan.FromSeconds(role.TokenTtlSeconds) : TimeSpan.FromHours(1);

        // If an identity alias maps this role to an entity, the token inherits the entity's
        // (and its groups') policies on top of the role's (ADR-0008/0009).
        var policies = role.Policies;
        if (identity is not null && await identity.ResolveAliasAsync($"approle/{name}", ct) is { } entityId)
            policies = await identity.ResolveEffectivePoliciesAsync(entityId, role.Policies, ct);

        return await tokens.CreateServiceTokenAsync(policies, ttl, ct);
    }

    private async Task<RoleData?> ReadRoleAsync(string name, CancellationToken ct)
    {
        var bytes = await store.GetBlobAsync(RoleKey(name), ct);
        return bytes is null ? null : JsonSerializer.Deserialize<RoleData>(bytes);
    }

    private string SecretIdKey(string name, string secretId)
    {
        var hmac = Convert.ToHexString(crypto.Hmac(Encoding.UTF8.GetBytes(secretId), keys.DeriveSubkey("approle-secret-id")));
        return $"auth/approle/secret-id/{name}/{hmac}";
    }

    private static string RoleKey(string name) => $"auth/approle/role/{name}";
    private static string RoleIdKey(string roleId) => $"auth/approle/role-id/{roleId}";

    private string RandomHex(int byteCount)
    {
        var b = new byte[byteCount];
        crypto.GetRandomBytes(b);
        return Convert.ToHexString(b);
    }

    private sealed record RoleData
    {
        public required string RoleId { get; init; }
        public required IReadOnlyList<string> Policies { get; init; }
        public long TokenTtlSeconds { get; init; }
        public long SecretIdTtlSeconds { get; init; }
        public int SecretIdNumUses { get; init; }
    }

    private sealed record SecretIdData
    {
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
        public int RemainingUses { get; init; } // 0 = unlimited
    }
}
