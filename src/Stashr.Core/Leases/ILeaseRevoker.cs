namespace Stashr.Core.Leases;

/// <summary>
/// Knows how to revoke a particular kind of lease (ADR-0006) — e.g. the database engine drops
/// the role it created. The lease manager calls this on explicit revoke or on expiry, passing
/// back the (non-secret) data captured when the lease was created.
/// </summary>
public interface ILeaseRevoker
{
    /// <summary>The lease type this revoker handles (e.g. "database").</summary>
    string LeaseType { get; }

    Task RevokeAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct = default);
}
