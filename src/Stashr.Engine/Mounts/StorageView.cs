using System.Text.Json;
using Stashr.Core.Cryptography;
using Stashr.Core.Storage;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Mounts;

/// <summary>
/// A prefix-isolated, encrypted-at-rest <see cref="IStorageView"/> over the blob store
/// (ADR-0009/0004). Every value an engine writes is envelope-encrypted under the engine DEK
/// before it touches the store, and decrypted on read — so cubbyhole/transit/etc. secrets are
/// never plaintext at rest. Keys are mount-prefixed so an engine can't reach another's data.
/// Requires the engine to be unsealed.
/// </summary>
public sealed class StorageView : IStorageView
{
    private readonly ISecretStore _store;
    private readonly KeyManager _keys;
    private readonly string _prefix;

    public StorageView(ISecretStore store, KeyManager keys, string prefix)
    {
        _store = store;
        _keys = keys;
        _prefix = prefix.EndsWith('/') ? prefix : prefix + "/";
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        var stored = await _store.GetBlobAsync(_prefix + key, ct).ConfigureAwait(false);
        if (stored is null) return null;
        var blob = JsonSerializer.Deserialize<SealedBlob>(stored)!;
        return _keys.DecryptValue(blob);
    }

    public async Task PutAsync(string key, byte[] value, CancellationToken ct = default)
    {
        var blob = _keys.EncryptValue(value);
        await _store.PutBlobAsync(_prefix + key, JsonSerializer.SerializeToUtf8Bytes(blob), ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => _store.DeleteBlobAsync(_prefix + key, ct);

    public async Task<IReadOnlyList<string>> ListAsync(string keyPrefix, CancellationToken ct = default)
    {
        var full = await _store.ListBlobAsync(_prefix + keyPrefix, ct).ConfigureAwait(false);
        return full.Select(k => k.Substring(_prefix.Length)).ToList();
    }
}
