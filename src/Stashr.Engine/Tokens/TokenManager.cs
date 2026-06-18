using System.Security.Cryptography;
using System.Text;
using Stashr.Core.Cryptography;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Tokens;

/// <summary>
/// Issues and validates service tokens (ADR-0006). The token string is
/// <c>s.{accessor}.{secret}</c>; the store keeps only the token's HMAC (keyed by a DEK-derived
/// subkey) plus the accessor used for audit/revocation — never the token itself. Batch tokens
/// are a later addition.
/// </summary>
public sealed class TokenManager(ISecretStore store, KeyManager keys, ICryptoProvider crypto)
{
    /// <summary>Create a service token; returns the one-time token string and its stored record.</summary>
    public async Task<(string Token, TokenInfo Info)> CreateServiceTokenAsync(
        IReadOnlyList<string> policies, TimeSpan ttl, CancellationToken ct = default)
    {
        var accessorBytes = new byte[16];
        var secretBytes = new byte[32];
        crypto.GetRandomBytes(accessorBytes);
        crypto.GetRandomBytes(secretBytes);

        var accessor = Convert.ToHexString(accessorBytes);
        var token = $"s.{accessor}.{Convert.ToHexString(secretBytes)}";
        var now = DateTimeOffset.UtcNow;

        var info = new TokenInfo
        {
            Accessor = accessor,
            TokenHmac = HmacToken(token),
            Policies = policies,
            Type = TokenType.Service,
            Ttl = ttl,
            MaxTtl = ttl,
            IssuedAt = now,
            ExpiresAt = now + ttl,
        };
        await store.PutTokenAsync(info, ct);
        return (token, info);
    }

    /// <summary>Validate a presented token. Returns its record, or null if invalid/expired.</summary>
    public async Task<TokenInfo?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "s") return null;

        var info = await store.GetTokenByAccessorAsync(parts[1], ct);
        if (info is null) return null;

        if (DateTimeOffset.UtcNow > info.ExpiresAt)
        {
            await store.DeleteTokenAsync(info.Accessor, ct);
            return null;
        }

        var expected = HmacToken(token);
        return CryptographicOperations.FixedTimeEquals(expected, info.TokenHmac) ? info : null;
    }

    public async Task RevokeAsync(string accessor, CancellationToken ct = default)
    {
        await store.DeleteTokenAsync(accessor, ct);

        // Cascading hard-delete of the token's cubbyhole (ADR-0011).
        var prefix = $"mounts/cubbyhole/{accessor}/";
        foreach (var key in await store.ListBlobAsync(prefix, ct))
            await store.DeleteBlobAsync(key, ct);
    }

    private byte[] HmacToken(string token)
        => crypto.Hmac(Encoding.UTF8.GetBytes(token), keys.DeriveSubkey("token-hmac"));
}
