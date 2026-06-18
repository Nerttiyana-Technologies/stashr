using System.Text.Json;
using Stashr.Core.Engines;
using Stashr.Core.Storage;
using Stashr.Crypto;
using Stashr.Engine.Mounts;
using Stashr.Engine.Seal;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class MountSystemTests
{
    /// <summary>A trivial engine that stores the request data as a JSON blob keyed by path.</summary>
    private sealed class EchoEngine : ISecretsEngine
    {
        public string Type => "echo";

        public async Task<EngineResponse> HandleAsync(EngineRequest request, IStorageView storage, CancellationToken ct = default)
        {
            switch (request.Operation)
            {
                case EngineOp.Write:
                    await storage.PutAsync(request.Path,
                        JsonSerializer.SerializeToUtf8Bytes(request.Data ?? new Dictionary<string, string>()), ct);
                    return EngineResponse.Empty;
                case EngineOp.Read:
                    var bytes = await storage.GetAsync(request.Path, ct);
                    if (bytes is null) return EngineResponse.Missing;
                    return new EngineResponse { Data = JsonSerializer.Deserialize<Dictionary<string, string>>(bytes) };
                case EngineOp.List:
                    return new EngineResponse { Keys = await storage.ListAsync(request.Path, ct) };
                case EngineOp.Delete:
                    await storage.DeleteAsync(request.Path, ct);
                    return EngineResponse.Empty;
                default:
                    return EngineResponse.Missing;
            }
        }
    }

    // The router now hands engines an encrypted-at-rest StorageView, which needs an unsealed DEK.
    private static async Task<Router> NewRouterAsync(InMemorySecretStore store)
    {
        var keys = new KeyManager(store, new OsCryptoProvider());
        await keys.InitializeAsync(5, 3);
        return new Router(store, keys);
    }

    [Fact]
    public async Task Router_dispatches_to_mounted_engine_and_round_trips()
    {
        var store = new InMemorySecretStore();
        var router = await NewRouterAsync(store);
        router.MountEngine("kv/", new EchoEngine());

        await router.RouteAsync(EngineOp.Write, "kv/foo", new Dictionary<string, string> { ["a"] = "1" });
        var read = await router.RouteAsync(EngineOp.Read, "kv/foo");

        Assert.False(read.NotFound);
        Assert.Equal("1", read.Data!["a"]);
    }

    [Fact]
    public async Task Mounted_engine_data_is_encrypted_at_rest()
    {
        var store = new InMemorySecretStore();
        var router = await NewRouterAsync(store);
        router.MountEngine("kv/", new EchoEngine());

        await router.RouteAsync(EngineOp.Write, "kv/foo", new Dictionary<string, string> { ["secret"] = "p@ssw0rd" });

        // The raw blob must not contain the plaintext secret.
        var raw = await store.GetBlobAsync("mounts/kv/foo");
        Assert.NotNull(raw);
        Assert.DoesNotContain("p@ssw0rd", System.Text.Encoding.UTF8.GetString(raw!));
    }

    [Fact]
    public async Task Unmounted_path_returns_missing()
    {
        var store = new InMemorySecretStore();
        var router = await NewRouterAsync(store);
        router.MountEngine("kv/", new EchoEngine());

        var r = await router.RouteAsync(EngineOp.Read, "nope/foo");
        Assert.True(r.NotFound);
    }

    [Fact]
    public async Task Mounts_are_storage_isolated()
    {
        var store = new InMemorySecretStore();
        var router = await NewRouterAsync(store);
        router.MountEngine("a/", new EchoEngine());
        router.MountEngine("b/", new EchoEngine());

        await router.RouteAsync(EngineOp.Write, "a/secret", new Dictionary<string, string> { ["v"] = "from-a" });

        Assert.True((await router.RouteAsync(EngineOp.Read, "b/secret")).NotFound);
        Assert.Equal("from-a", (await router.RouteAsync(EngineOp.Read, "a/secret")).Data!["v"]);
    }

    [Fact]
    public async Task Longest_prefix_mount_wins()
    {
        var store = new InMemorySecretStore();
        var router = await NewRouterAsync(store);
        router.MountEngine("secret/", new EchoEngine());
        router.MountEngine("secret/special/", new EchoEngine());

        await router.RouteAsync(EngineOp.Write, "secret/special/x", new Dictionary<string, string> { ["v"] = "1" });

        Assert.True((await router.RouteAsync(EngineOp.Read, "secret/special/y")).NotFound);
        Assert.False((await router.RouteAsync(EngineOp.Read, "secret/special/x")).NotFound);
    }

    [Fact]
    public async Task Blob_storage_round_trips_directly()
    {
        var store = new InMemorySecretStore();
        await store.PutBlobAsync("k1", new byte[] { 1, 2, 3 });
        await store.PutBlobAsync("k2", new byte[] { 4 });

        Assert.Equal(new byte[] { 1, 2, 3 }, await store.GetBlobAsync("k1"));
        Assert.Contains("k1", await store.ListBlobAsync("k"));

        await store.DeleteBlobAsync("k1");
        Assert.Null(await store.GetBlobAsync("k1"));
    }
}
