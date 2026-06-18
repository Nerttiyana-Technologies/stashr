using Stashr.Core.Model;
using Stashr.Crypto;
using Stashr.Engine.Audit;
using Stashr.Engine.Seal;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class AuditChainTests
{
    private static async Task<(InMemorySecretStore store, OsCryptoProvider crypto, KeyManager km)> UnsealedAsync()
    {
        var store = new InMemorySecretStore();
        var crypto = new OsCryptoProvider();
        var km = new KeyManager(store, crypto);
        await km.InitializeAsync(5, 3);
        return (store, crypto, km);
    }

    private static AuditEntry Entry(string op) =>
        new() { Seq = 0, RequestId = Guid.NewGuid().ToString("n"), Type = "request", Operation = op };

    [Fact]
    public async Task Appended_chain_verifies()
    {
        var (store, crypto, km) = await UnsealedAsync();
        var chain = new AuditChain(store, crypto, km);

        await chain.AppendAsync(Entry("read"));
        await chain.AppendAsync(Entry("write"));
        await chain.AppendAsync(Entry("delete"));

        var result = await chain.VerifyAsync();
        Assert.True(result.Valid);
        Assert.Equal(3, result.Checked);
    }

    [Fact]
    public async Task Sequence_increments_and_hash_is_set()
    {
        var (store, crypto, km) = await UnsealedAsync();
        var chain = new AuditChain(store, crypto, km);

        var a = await chain.AppendAsync(Entry("read"));
        var b = await chain.AppendAsync(Entry("write"));

        Assert.Equal(1, a.Seq);
        Assert.Equal(2, b.Seq);
        Assert.False(string.IsNullOrEmpty(b.ChainHash));
    }

    [Fact]
    public async Task Tampered_entry_is_detected()
    {
        var (store, crypto, km) = await UnsealedAsync();
        var chain = new AuditChain(store, crypto, km);

        await chain.AppendAsync(Entry("read"));
        await chain.AppendAsync(Entry("write"));

        // Forge a row straight into the store with a bogus chain hash.
        await store.AppendAuditAsync(new AuditEntry
        {
            Seq = 3, RequestId = "forged", Type = "request", Operation = "write",
            ChainHash = "this-is-not-a-valid-chain-hash",
        });

        var result = await chain.VerifyAsync();
        Assert.False(result.Valid);
        Assert.Equal(3, result.FirstBrokenSeq);
    }
}
