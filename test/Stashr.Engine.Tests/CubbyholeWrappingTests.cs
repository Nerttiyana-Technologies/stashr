using Stashr.Core.Engines;
using Stashr.Crypto;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class CubbyholeWrappingTests
{
    private static StashrEngine NewEngine() => new(new InMemorySecretStore(), new OsCryptoProvider());

    private static Dictionary<string, string> D(string k, string v) => new() { [k] = v };

    [Fact]
    public async Task Cubbyhole_is_isolated_per_token()
    {
        var engine = NewEngine();
        await engine.Keys.InitializeAsync(5, 3); // StorageView encrypts at rest → needs the DEK

        await engine.Router.RouteAsync(EngineOp.Write, "cubbyhole/note", D("v", "alice"), "acc-alice");
        await engine.Router.RouteAsync(EngineOp.Write, "cubbyhole/note", D("v", "bob"), "acc-bob");

        var alice = await engine.Router.RouteAsync(EngineOp.Read, "cubbyhole/note", null, "acc-alice");
        var bob = await engine.Router.RouteAsync(EngineOp.Read, "cubbyhole/note", null, "acc-bob");

        Assert.Equal("alice", alice.Data!["v"]);
        Assert.Equal("bob", bob.Data!["v"]);
    }

    [Fact]
    public async Task Wrapping_unwraps_once_then_fails()
    {
        var engine = NewEngine();
        await engine.Keys.InitializeAsync(5, 3);

        var token = await engine.Wrapping.WrapAsync(D("secret_id", "xyz"), TimeSpan.FromMinutes(5));

        var first = await engine.Wrapping.UnwrapAsync(token);
        Assert.Equal("xyz", first!["secret_id"]);

        // Single use: a second unwrap (or an interceptor) gets nothing.
        Assert.Null(await engine.Wrapping.UnwrapAsync(token));
    }

    [Fact]
    public async Task Revoking_a_token_purges_its_cubbyhole()
    {
        var engine = NewEngine();
        await engine.Keys.InitializeAsync(5, 3);

        var (_, info) = await engine.Tokens.CreateServiceTokenAsync(new[] { "p" }, TimeSpan.FromHours(1));
        await engine.Router.RouteAsync(EngineOp.Write, "cubbyhole/x", D("v", "1"), info.Accessor);
        Assert.Equal("1", (await engine.Router.RouteAsync(EngineOp.Read, "cubbyhole/x", null, info.Accessor)).Data!["v"]);

        await engine.Tokens.RevokeAsync(info.Accessor);

        Assert.True((await engine.Router.RouteAsync(EngineOp.Read, "cubbyhole/x", null, info.Accessor)).NotFound);
    }
}
