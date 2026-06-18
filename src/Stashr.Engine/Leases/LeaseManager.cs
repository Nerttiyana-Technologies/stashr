using System.Text.Json;
using Stashr.Core.Leases;
using Stashr.Core.Storage;

namespace Stashr.Engine.Leases;

/// <summary>A tracked, bounded-lifetime grant (ADR-0006).</summary>
public sealed record LeaseInfo
{
    public required string LeaseId { get; init; }
    public required string Type { get; init; }
    public required IReadOnlyDictionary<string, string> Data { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public long TtlSeconds { get; init; }

    public DateTimeOffset ExpiresAt => IssuedAt.AddSeconds(TtlSeconds);
}

/// <summary>
/// The expiration manager (ADR-0006): tracks leases, auto-revokes on expiry, and dispatches
/// revocation to a type-specific <see cref="ILeaseRevoker"/> (e.g. the database engine drops the
/// role it created). Lease records hold only non-secret data (a username to drop, a role name),
/// so they are stored as plain blobs.
/// </summary>
public sealed class LeaseManager(ISecretStore store)
{
    private const string Prefix = "sys/leases/";
    private readonly Dictionary<string, ILeaseRevoker> _revokers = new(StringComparer.Ordinal);

    public void RegisterRevoker(ILeaseRevoker revoker) => _revokers[revoker.LeaseType] = revoker;

    public async Task<LeaseInfo> CreateAsync(
        string type, IReadOnlyDictionary<string, string> data, TimeSpan ttl, CancellationToken ct = default)
    {
        var lease = new LeaseInfo
        {
            LeaseId = Guid.NewGuid().ToString("n"),
            Type = type,
            Data = data,
            IssuedAt = DateTimeOffset.UtcNow,
            TtlSeconds = (long)ttl.TotalSeconds,
        };
        await store.PutBlobAsync(Prefix + lease.LeaseId, JsonSerializer.SerializeToUtf8Bytes(lease), ct);
        return lease;
    }

    /// <summary>Revoke a lease now: run its type's revoker, then delete it. Returns false if unknown.</summary>
    public async Task<bool> RevokeAsync(string leaseId, CancellationToken ct = default)
    {
        var blob = await store.GetBlobAsync(Prefix + leaseId, ct);
        if (blob is null) return false;

        var lease = JsonSerializer.Deserialize<LeaseInfo>(blob)!;
        if (_revokers.TryGetValue(lease.Type, out var revoker))
            await revoker.RevokeAsync(lease.Data, ct);

        await store.DeleteBlobAsync(Prefix + leaseId, ct);
        return true;
    }

    /// <summary>Revoke every expired lease. Returns the number revoked. Called by the sweep loop.</summary>
    public async Task<int> RevokeExpiredAsync(CancellationToken ct = default)
    {
        var revoked = 0;
        foreach (var key in await store.ListBlobAsync(Prefix, ct))
        {
            var blob = await store.GetBlobAsync(key, ct);
            if (blob is null) continue;
            var lease = JsonSerializer.Deserialize<LeaseInfo>(blob)!;
            if (DateTimeOffset.UtcNow >= lease.ExpiresAt)
            {
                await RevokeAsync(lease.LeaseId, ct);
                revoked++;
            }
        }
        return revoked;
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        => (await store.ListBlobAsync(Prefix, ct)).Select(k => k.Substring(Prefix.Length)).ToList();
}
