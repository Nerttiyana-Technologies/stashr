using Stashr.Core.Ha;
using Stashr.Crypto;
using Stashr.Engine.Ha;
using Stashr.Engine.Seal;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class HaCoordinatorTests
{
    private sealed class FakeElection : ILeaderElection
    {
        public long Epoch { get; set; }
        public bool AcquireResult { get; set; }
        public bool HeldResult { get; set; } = true;

        public Task<bool> TryAcquireAsync(CancellationToken ct = default) => Task.FromResult(AcquireResult);
        public Task<bool> IsHeldAsync(CancellationToken ct = default) => Task.FromResult(HeldResult);
        public Task ReleaseAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<KeyManager> UnsealedKeysAsync()
    {
        var km = new KeyManager(new InMemorySecretStore(), new OsCryptoProvider());
        await km.InitializeAsync(5, 3);
        return km;
    }

    [Fact]
    public async Task Becomes_active_when_leadership_is_acquired()
    {
        using var keys = await UnsealedKeysAsync();
        var election = new FakeElection { AcquireResult = true, Epoch = 7 };
        var ha = new HaCoordinator(election, keys);

        Assert.False(ha.IsLeader);
        await ha.TickAsync();

        Assert.True(ha.IsLeader);
        Assert.Equal(7, ha.Epoch);
        Assert.Equal("active", ha.Role);
    }

    [Fact]
    public async Task Stays_standby_when_lock_is_unavailable()
    {
        using var keys = await UnsealedKeysAsync();
        var election = new FakeElection { AcquireResult = false };
        var ha = new HaCoordinator(election, keys);

        await ha.TickAsync();

        Assert.False(ha.IsLeader);
        Assert.Equal("standby", ha.Role);
        Assert.False(keys.IsSealed); // a node that never led is not force-sealed
    }

    [Fact]
    public async Task Self_seals_when_leadership_is_lost()
    {
        using var keys = await UnsealedKeysAsync();
        var election = new FakeElection { AcquireResult = true, HeldResult = true };
        var ha = new HaCoordinator(election, keys);

        await ha.TickAsync();          // becomes leader
        Assert.True(ha.IsLeader);
        Assert.False(keys.IsSealed);

        election.HeldResult = false;   // lock lost (partition / DB outage)
        await ha.TickAsync();

        Assert.False(ha.IsLeader);
        Assert.True(keys.IsSealed);    // fail-closed
    }
}
