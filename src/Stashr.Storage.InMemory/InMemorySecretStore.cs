using System.Runtime.CompilerServices;
using Stashr.Core;
using Stashr.Core.Model;
using Stashr.Core.Storage;

namespace Stashr.Storage.InMemory;

/// <summary>
/// A thread-safe, ephemeral <see cref="ISecretStore"/> kept entirely in process memory.
/// Used by unit tests and by <c>stashr server -dev</c> (ADR-0014). All state is lost on
/// shutdown; never use it in production.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly object _gate = new();

    private readonly Dictionary<int, WrappedKey> _keys = new();
    private byte[]? _sealConfig;

    private readonly Dictionary<string, PathEntry> _kv = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Policy> _policies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TokenInfo> _tokens = new(StringComparer.Ordinal);

    private readonly List<AuditEntry> _audit = new();
    private long _auditSeq;

    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    // --- key ring ---

    public Task<WrappedKey?> GetActiveKeyAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            WrappedKey? active = null;
            foreach (var k in _keys.Values)
                if (k.State == "active" && (active is null || k.Version > active.Version))
                    active = k;
            return Task.FromResult(active);
        }
    }

    public Task<WrappedKey?> GetKeyVersionAsync(int version, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_keys.TryGetValue(version, out var k) ? k : null);
    }

    public Task PutKeyAsync(WrappedKey key, CancellationToken ct = default)
    {
        lock (_gate) _keys[key.Version] = key;
        return Task.CompletedTask;
    }

    // --- seal config ---

    public Task<byte[]?> GetSealConfigAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_sealConfig is null ? null : (byte[])_sealConfig.Clone());
    }

    public Task PutSealConfigAsync(byte[] sealedRoot, CancellationToken ct = default)
    {
        lock (_gate) _sealConfig = (byte[])sealedRoot.Clone();
        return Task.CompletedTask;
    }

    // --- KV ---

    public Task<SecretMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_kv.TryGetValue(path, out var e) ? e.Meta : null);
    }

    public Task<SecretRecord?> GetVersionAsync(string path, int? version, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_kv.TryGetValue(path, out var e)) return Task.FromResult<SecretRecord?>(null);
            var v = version ?? e.Meta.CurrentVersion;
            return Task.FromResult(e.Versions.TryGetValue(v, out var rec) ? rec : null);
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var prefix = pathPrefix.EndsWith('/') || pathPrefix.Length == 0 ? pathPrefix : pathPrefix + "/";
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var fullPath in _kv.Keys)
            {
                if (!fullPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var remainder = fullPath.Substring(prefix.Length);
                var slash = remainder.IndexOf('/');
                // Vault-style listing: immediate children; folders get a trailing slash.
                keys.Add(slash < 0 ? remainder : remainder.Substring(0, slash + 1));
            }
            return Task.FromResult<IReadOnlyList<string>>(keys.ToList());
        }
    }

    public Task<int> PutVersionAsync(SecretRecord record, int? expectedCurrentVersion, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_kv.TryGetValue(record.Path, out var e))
            {
                e = new PathEntry
                {
                    Meta = new SecretMetadata
                    {
                        Path = record.Path,
                        CurrentVersion = 0,
                        CreatedAt = record.CreatedAt,
                        UpdatedAt = record.CreatedAt,
                    },
                };
                _kv[record.Path] = e;
            }

            if (expectedCurrentVersion is { } cas && cas != e.Meta.CurrentVersion)
                throw new CasMismatchException(cas, e.Meta.CurrentVersion);

            var next = e.Meta.CurrentVersion + 1;
            var stored = record with { Version = next };
            e.Versions[next] = stored;
            e.Meta = e.Meta with { CurrentVersion = next, UpdatedAt = record.CreatedAt };

            // Prune history beyond MaxVersions (destroy oldest).
            while (e.Versions.Count > e.Meta.MaxVersions)
            {
                var oldest = int.MaxValue;
                foreach (var key in e.Versions.Keys) if (key < oldest) oldest = key;
                e.Versions.Remove(oldest);
            }

            return Task.FromResult(next);
        }
    }

    public Task SoftDeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => MutateVersions(path, versions, r => r with { DeletedAt = DateTimeOffset.UtcNow });

    public Task UndeleteAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => MutateVersions(path, versions, r => r with { DeletedAt = null });

    public Task DestroyAsync(string path, IReadOnlyList<int> versions, CancellationToken ct = default)
        => MutateVersions(path, versions, r => r with { Destroyed = true });

    private Task MutateVersions(string path, IReadOnlyList<int> versions, Func<SecretRecord, SecretRecord> mutate)
    {
        lock (_gate)
        {
            if (_kv.TryGetValue(path, out var e))
                foreach (var v in versions)
                    if (e.Versions.TryGetValue(v, out var rec))
                        e.Versions[v] = mutate(rec);
        }
        return Task.CompletedTask;
    }

    // --- policies ---

    public Task<Policy?> GetPolicyAsync(string name, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_policies.TryGetValue(name, out var p) ? p : null);
    }

    public Task PutPolicyAsync(Policy policy, CancellationToken ct = default)
    {
        lock (_gate) _policies[policy.Name] = policy;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListPoliciesAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<string>>(_policies.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    // --- tokens ---

    public Task<TokenInfo?> GetTokenByAccessorAsync(string accessor, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_tokens.TryGetValue(accessor, out var t) ? t : null);
    }

    public Task PutTokenAsync(TokenInfo token, CancellationToken ct = default)
    {
        lock (_gate) _tokens[token.Accessor] = token;
        return Task.CompletedTask;
    }

    public Task DeleteTokenAsync(string accessor, CancellationToken ct = default)
    {
        lock (_gate) _tokens.Remove(accessor);
        return Task.CompletedTask;
    }

    // --- audit ---

    public Task AppendAuditAsync(AuditEntry entry, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _auditSeq = entry.Seq;
            _audit.Add(entry);
        }
        return Task.CompletedTask;
    }

    public Task<long> GetLastAuditSeqAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_auditSeq);
    }

    public async IAsyncEnumerable<AuditEntry> ReadAuditAsync(
        long fromSeq, [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<AuditEntry> snapshot;
        lock (_gate) snapshot = _audit.Where(a => a.Seq >= fromSeq).ToList();

        foreach (var e in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
        await Task.CompletedTask;
    }

    // --- generic blob storage (ADR-0009) ---

    public Task<byte[]?> GetBlobAsync(string key, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<byte[]?>(_blobs.TryGetValue(key, out var v) ? (byte[])v.Clone() : null);
    }

    public Task PutBlobAsync(string key, byte[] value, CancellationToken ct = default)
    {
        lock (_gate) _blobs[key] = (byte[])value.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListBlobAsync(string keyPrefix, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var keys = _blobs.Keys
                .Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(keys);
        }
    }

    public Task DeleteBlobAsync(string key, CancellationToken ct = default)
    {
        lock (_gate) _blobs.Remove(key);
        return Task.CompletedTask;
    }

    private sealed class PathEntry
    {
        public required SecretMetadata Meta { get; set; }
        public Dictionary<int, SecretRecord> Versions { get; } = new();
    }
}
