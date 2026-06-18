using Stashr.Crypto;
using Stashr.Engine.Kv;
using Stashr.Engine.Seal;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class KvSecretsEngineTests
{
    private static async Task<KvSecretsEngine> NewAsync()
    {
        var store = new InMemorySecretStore();
        var km = new KeyManager(store, new OsCryptoProvider());
        await km.InitializeAsync(5, 3);
        return new KvSecretsEngine(store, km);
    }

    [Fact]
    public async Task Write_then_read_returns_decrypted_values()
    {
        var kv = await NewAsync();
        var data = new Dictionary<string, string> { ["password"] = "p@ss", ["host"] = "db1" };

        var v = await kv.WriteAsync("secret/app/db", data);
        Assert.Equal(1, v);

        var read = await kv.ReadAsync("secret/app/db");
        Assert.NotNull(read);
        Assert.Equal("p@ss", read!.Data["password"]);
        Assert.Equal("db1", read.Data["host"]);
    }

    [Fact]
    public async Task Each_write_creates_a_new_version()
    {
        var kv = await NewAsync();
        await kv.WriteAsync("secret/app/db", new Dictionary<string, string> { ["v"] = "1" });
        var v2 = await kv.WriteAsync("secret/app/db", new Dictionary<string, string> { ["v"] = "2" });

        Assert.Equal(2, v2);
        Assert.Equal("2", (await kv.ReadAsync("secret/app/db"))!.Data["v"]);
        Assert.Equal("1", (await kv.ReadAsync("secret/app/db", version: 1))!.Data["v"]);
    }

    [Fact]
    public async Task Soft_deleted_version_returns_metadata_without_data()
    {
        var kv = await NewAsync();
        await kv.WriteAsync("secret/app/db", new Dictionary<string, string> { ["v"] = "1" });

        await kv.SoftDeleteAsync("secret/app/db", new[] { 1 });

        var read = await kv.ReadAsync("secret/app/db", version: 1);
        Assert.NotNull(read);
        Assert.NotNull(read!.DeletedAt);
        Assert.Empty(read.Data);
    }

    [Fact]
    public async Task Read_missing_path_returns_null()
    {
        var kv = await NewAsync();
        Assert.Null(await kv.ReadAsync("secret/does/not/exist"));
    }
}
