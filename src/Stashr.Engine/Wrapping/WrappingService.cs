using System.Text.Json;
using Stashr.Core.Cryptography;
using Stashr.Core.Storage;
using Stashr.Engine.Seal;
using Stashr.Engine.Tokens;

namespace Stashr.Engine.Wrapping;

/// <summary>
/// Response wrapping (ADR-0011): stash a payload behind a single-use, short-TTL wrapping token.
/// The payload is envelope-encrypted under the DEK at rest (never plaintext, ADR-0004). The
/// recipient unwraps once; a second unwrap (or interception) fails, making delivery
/// tamper-evident. Used for AppRole's wrapped <c>secret_id</c> (secret-zero).
/// </summary>
public sealed class WrappingService(ISecretStore store, KeyManager keys, TokenManager tokens)
{
    private const string Prefix = "sys/wrapping/";

    /// <summary>Wrap data; returns the one-time wrapping token.</summary>
    public async Task<string> WrapAsync(IReadOnlyDictionary<string, string> data, TimeSpan ttl, CancellationToken ct = default)
    {
        var (token, info) = await tokens.CreateServiceTokenAsync(Array.Empty<string>(), ttl, ct);
        var blob = keys.EncryptValue(JsonSerializer.SerializeToUtf8Bytes(data));
        await store.PutBlobAsync(Prefix + info.Accessor, JsonSerializer.SerializeToUtf8Bytes(blob), ct);
        return token;
    }

    /// <summary>Unwrap once. Returns null if the token is invalid/expired or already unwrapped.</summary>
    public async Task<IReadOnlyDictionary<string, string>?> UnwrapAsync(string wrappingToken, CancellationToken ct = default)
    {
        var info = await tokens.ValidateAsync(wrappingToken, ct);
        if (info is null) return null;

        var stored = await store.GetBlobAsync(Prefix + info.Accessor, ct);
        if (stored is null) return null; // already unwrapped

        await store.DeleteBlobAsync(Prefix + info.Accessor, ct);
        await tokens.RevokeAsync(info.Accessor, ct); // single use

        var blob = JsonSerializer.Deserialize<SealedBlob>(stored)!;
        var plaintext = keys.DecryptValue(blob);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext);
    }

    /// <summary>True if the wrapping token is valid and not yet unwrapped.</summary>
    public async Task<bool> LookupAsync(string wrappingToken, CancellationToken ct = default)
    {
        var info = await tokens.ValidateAsync(wrappingToken, ct);
        return info is not null && await store.GetBlobAsync(Prefix + info.Accessor, ct) is not null;
    }
}
