namespace Stashr.Core.Storage;

/// <summary>
/// A prefix-isolated view of the blob store handed to a mounted engine (ADR-0009). An engine
/// can only read/write keys within its own mount — it physically cannot reach another mount's
/// data. Keys are relative to the mount.
/// </summary>
public interface IStorageView
{
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);
    Task PutAsync(string key, byte[] value, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(string keyPrefix, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
