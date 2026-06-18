using Stashr.Storage.Postgres;
using Testcontainers.PostgreSql;
using Xunit;

namespace Stashr.Storage.Postgres.Tests;

/// <summary>
/// Real two-"node" HA election against one Postgres (Testcontainers): two election instances
/// contend for the advisory lock — only one wins, and when it steps down the other is promoted,
/// with the fencing epoch advancing (ADR-0002).
/// </summary>
public class PostgresLeaderElectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Only_one_node_leads_and_failover_advances_the_epoch()
    {
        var cs = _pg.GetConnectionString();
        await using var node1 = new PostgresLeaderElection(cs);
        await using var node2 = new PostgresLeaderElection(cs);

        Assert.True(await node1.TryAcquireAsync());   // node1 becomes leader
        Assert.False(await node2.TryAcquireAsync());  // node2 blocked — lock held
        Assert.True(await node1.IsHeldAsync());

        var epoch1 = node1.Epoch;

        await node1.ReleaseAsync();                    // node1 steps down (e.g. crash/shutdown)
        Assert.False(await node1.IsHeldAsync());

        Assert.True(await node2.TryAcquireAsync());    // node2 is promoted
        Assert.True(node2.Epoch > epoch1);             // fencing epoch advanced
    }
}
