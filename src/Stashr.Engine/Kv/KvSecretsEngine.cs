using System.Text.Json;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Kv;

/// <summary>
/// The KV v2 secrets engine (ADR-0010). Stores versioned key/value secrets, encrypting the
/// value map under the active DEK (ADR-0004) before it reaches the store. Reads decrypt only
/// for live versions; deleted/destroyed versions return metadata with no data.
/// </summary>
public sealed class KvSecretsEngine(ISecretStore store, KeyManager keys)
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    /// <summary>Write a new version. Pass <paramref name="cas"/> to require check-and-set (ADR-0010).</summary>
    public async Task<int> WriteAsync(
        string path, IReadOnlyDictionary<string, string> data, int? cas = null, CancellationToken ct = default)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data);
        var blob = keys.EncryptValue(plaintext);
        var record = new SecretRecord
        {
            Path = path,
            Version = 0, // assigned by the store
            Ciphertext = blob,
            KeyVersion = keys.ActiveKeyVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return await store.PutVersionAsync(record, cas, ct);
    }

    /// <summary>Read a version (latest if <paramref name="version"/> is null). Returns null if absent.</summary>
    public async Task<SecretVersion?> ReadAsync(string path, int? version = null, CancellationToken ct = default)
    {
        var record = await store.GetVersionAsync(path, version, ct);
        if (record is null) return null;

        if (record.Destroyed || record.DeletedAt is not null)
            return new SecretVersion
            {
                Version = record.Version,
                Data = Empty,
                CreatedAt = record.CreatedAt,
                DeletedAt = record.DeletedAt,
                Destroyed = record.Destroyed,
            };

        var plaintext = keys.DecryptValue(record.Ciphertext);
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext) ?? new Dictionary<string, string>();
        return new SecretVersion
        {
            Version = record.Version,
            Data = data,
            CreatedAt = record.CreatedAt,
        };
    }

    public Task<SecretMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
        => store.GetMetadataAsync(path, ct);

    public Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken ct = default)
        => store.ListAsync(pathPrefix, ct);

    public Task SoftDeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => store.SoftDeleteAsync(path, versions, ct);

    public Task UndeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => store.UndeleteAsync(path, versions, ct);

    public Task DestroyAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => store.DestroyAsync(path, versions, ct);
}
