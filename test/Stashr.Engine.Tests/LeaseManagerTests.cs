using Stashr.Core.Leases;
using Stashr.Engine.Leases;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class LeaseManagerTests
{
    private sealed class FakeRevoker : ILeaseRevoker
    {
        public string LeaseType => "test";
        public List<string> Revoked { get; } = new();

        public Task RevokeAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
        {
            Revoked.Add(data["id"]);
            return Task.CompletedTask;
        }
    }

    private static Dictionary<string, string> D(string id) => new() { ["id"] = id };

    [Fact]
    public async Task Create_then_revoke_invokes_the_revoker_and_removes_the_lease()
    {
        var mgr = new LeaseManager(new InMemorySecretStore());
        var revoker = new FakeRevoker();
        mgr.RegisterRevoker(revoker);

        var lease = await mgr.CreateAsync("test", D("user-1"), TimeSpan.FromMinutes(5));
        Assert.Contains(lease.LeaseId, await mgr.ListAsync());

        Assert.True(await mgr.RevokeAsync(lease.LeaseId));
        Assert.Contains("user-1", revoker.Revoked);
        Assert.DoesNotContain(lease.LeaseId, await mgr.ListAsync());
    }

    [Fact]
    public async Task Expired_leases_are_swept_and_revoked()
    {
        var mgr = new LeaseManager(new InMemorySecretStore());
        var revoker = new FakeRevoker();
        mgr.RegisterRevoker(revoker);

        await mgr.CreateAsync("test", D("expired"), TimeSpan.Zero);          // already expired
        await mgr.CreateAsync("test", D("live"), TimeSpan.FromMinutes(10));  // still valid

        var revoked = await mgr.RevokeExpiredAsync();

        Assert.Equal(1, revoked);
        Assert.Contains("expired", revoker.Revoked);
        Assert.DoesNotContain("live", revoker.Revoked);
    }

    [Fact]
    public async Task Revoking_unknown_lease_returns_false()
    {
        var mgr = new LeaseManager(new InMemorySecretStore());
        Assert.False(await mgr.RevokeAsync("does-not-exist"));
    }

    [Fact]
    public async Task Lease_without_registered_revoker_still_deletes()
    {
        var mgr = new LeaseManager(new InMemorySecretStore());
        var lease = await mgr.CreateAsync("unhandled", D("x"), TimeSpan.FromMinutes(5));

        Assert.True(await mgr.RevokeAsync(lease.LeaseId)); // no revoker, but lease is removed
        Assert.DoesNotContain(lease.LeaseId, await mgr.ListAsync());
    }
}
