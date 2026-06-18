using Stashr.Core.Cryptography;
using Stashr.Core.Model;

namespace Stashr.Core.Storage;

/// <summary>
/// Persistence boundary (ADR-0001/0007). Implementations store ONLY ciphertext, wrapped
/// keys, metadata and audit — never plaintext or the master key. The same interface backs
/// the in-memory (test), PostgreSQL and SQL Server providers.
/// </summary>
public interface ISecretStore
{
    /// <summary>Apply schema/migrations and ready the store.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    // --- key ring (wrapped KEK/DEK versions, ADR-0007) ---
    Task<WrappedKey?> GetActiveKeyAsync(CancellationToken ct = default);
    Task<WrappedKey?> GetKeyVersionAsync(int version, CancellationToken ct = default);
    Task PutKeyAsync(WrappedKey key, CancellationToken ct = default);

    // --- root/seal config (the Shamir-wrapped master key material, ADR-0002/0012) ---
    Task<byte[]?> GetSealConfigAsync(CancellationToken ct = default);
    Task PutSealConfigAsync(byte[] sealedRoot, CancellationToken ct = default);

    // --- KV secrets (ADR-0010) ---
    Task<SecretMetadata?> GetMetadataAsync(string path, CancellationToken ct = default);
    Task<SecretRecord?> GetVersionAsync(string path, int? version, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken ct = default);

    /// <summary>
    /// Append a new encrypted version. <paramref name="expectedCurrentVersion"/> enforces CAS
    /// when supplied; the store performs the check atomically and throws on mismatch (ADR-0010).
    /// Returns the new version number.
    /// </summary>
    Task<int> PutVersionAsync(SecretRecord record, int? expectedCurrentVersion, CancellationToken ct = default);

    Task SoftDeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default);
    Task UndeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default);
    Task DestroyAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default);

    // --- policies (ADR-0008) ---
    Task<Policy?> GetPolicyAsync(string name, CancellationToken ct = default);
    Task PutPolicyAsync(Policy policy, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListPoliciesAsync(CancellationToken ct = default);

    // --- tokens (ADR-0006) ---
    Task<TokenInfo?> GetTokenByAccessorAsync(string accessor, CancellationToken ct = default);
    Task PutTokenAsync(TokenInfo token, CancellationToken ct = default);
    Task DeleteTokenAsync(string accessor, CancellationToken ct = default);

    // --- audit (ADR-0005) ---
    Task AppendAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task<long> GetLastAuditSeqAsync(CancellationToken ct = default);
    IAsyncEnumerable<AuditEntry> ReadAuditAsync(long fromSeq, CancellationToken ct = default);

    // --- generic blob storage: the physical layer mounted engines build on (ADR-0009) ---
    Task<byte[]?> GetBlobAsync(string key, CancellationToken ct = default);
    Task PutBlobAsync(string key, byte[] value, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListBlobAsync(string keyPrefix, CancellationToken ct = default);
    Task DeleteBlobAsync(string key, CancellationToken ct = default);
}

/// <summary>A versioned wrapped key in the key ring (ADR-0007).</summary>
public sealed record WrappedKey
{
    public required int Version { get; init; }
    public required SealedBlob Wrapped { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string State { get; init; } = "active"; // active|deprecated|retired
}

/// <summary>The encrypted, at-rest form of a single secret version (ADR-0007/0010).</summary>
public sealed record SecretRecord
{
    public required string Path { get; init; }
    public required int Version { get; init; }
    public required SealedBlob Ciphertext { get; init; }
    public required int KeyVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public bool Destroyed { get; init; }
}
