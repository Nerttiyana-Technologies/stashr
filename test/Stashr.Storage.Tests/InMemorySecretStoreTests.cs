using Stashr.Core;
using Stashr.Core.Cryptography;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Storage.Tests;

public class InMemorySecretStoreTests
{
    private static SecretRecord Record(string path, int version = 0) => new()
    {
        Path = path,
        Version = version,
        KeyVersion = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        Ciphertext = new SealedBlob
        {
            Nonce = new byte[12],
            Ciphertext = new byte[] { 1, 2, 3 },
            Tag = new byte[16],
        },
    };

    [Fact]
    public async Task PutVersion_assigns_incrementing_versions()
    {
        var store = new InMemorySecretStore();

        var v1 = await store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: null);
        var v2 = await store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: null);

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);

        var meta = await store.GetMetadataAsync("secret/app/db");
        Assert.Equal(2, meta!.CurrentVersion);
    }

    [Fact]
    public async Task GetVersion_returns_latest_when_version_is_null()
    {
        var store = new InMemorySecretStore();
        await store.PutVersionAsync(Record("secret/app/db"), null);
        await store.PutVersionAsync(Record("secret/app/db"), null);

        var latest = await store.GetVersionAsync("secret/app/db", version: null);
        Assert.Equal(2, latest!.Version);
    }

    [Fact]
    public async Task Cas_mismatch_is_rejected()
    {
        var store = new InMemorySecretStore();
        await store.PutVersionAsync(Record("secret/app/db"), null); // now at v1

        await Assert.ThrowsAsync<CasMismatchException>(
            () => store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: 5));
    }

    [Fact]
    public async Task Cas_match_succeeds()
    {
        var store = new InMemorySecretStore();
        await store.PutVersionAsync(Record("secret/app/db"), null);      // v1, current=1
        var v2 = await store.PutVersionAsync(Record("secret/app/db"), expectedCurrentVersion: 1);
        Assert.Equal(2, v2);
    }

    [Fact]
    public async Task SoftDelete_then_undelete_round_trips()
    {
        var store = new InMemorySecretStore();
        await store.PutVersionAsync(Record("secret/app/db"), null);

        await store.SoftDeleteAsync("secret/app/db", new[] { 1 });
        Assert.NotNull((await store.GetVersionAsync("secret/app/db", 1))!.DeletedAt);

        await store.UndeleteAsync("secret/app/db", new[] { 1 });
        Assert.Null((await store.GetVersionAsync("secret/app/db", 1))!.DeletedAt);
    }

    [Fact]
    public async Task Destroy_marks_version_destroyed()
    {
        var store = new InMemorySecretStore();
        await store.PutVersionAsync(Record("secret/app/db"), null);

        await store.DestroyAsync("secret/app/db", new[] { 1 });
        Assert.True((await store.GetVersionAsync("secret/app/db", 1))!.Destroyed);
    }

    [Fact]
    public async Task List_returns_immediate_children_with_folder_slashes()
    {
        var store = new InMemorySecretStore();
        await store.PutVersionAsync(Record("secret/app/db"), null);
        await store.PutVersionAsync(Record("secret/app/cache"), null);
        await store.PutVersionAsync(Record("secret/other/x"), null);

        var children = await store.ListAsync("secret/app");
        Assert.Contains("db", children);
        Assert.Contains("cache", children);
        Assert.DoesNotContain("x", children);
    }

    [Fact]
    public async Task Policies_and_tokens_round_trip()
    {
        var store = new InMemorySecretStore();

        var policy = new Policy
        {
            Name = "app-read",
            Rules = new[] { new PolicyRule { PathPattern = "secret/app/*", Capabilities = Capability.Read } },
        };
        await store.PutPolicyAsync(policy);
        Assert.Equal("app-read", (await store.GetPolicyAsync("app-read"))!.Name);
        Assert.Contains("app-read", await store.ListPoliciesAsync());

        var token = new TokenInfo
        {
            Accessor = "acc-1",
            TokenHmac = new byte[32],
            Policies = new[] { "app-read" },
        };
        await store.PutTokenAsync(token);
        Assert.Equal("acc-1", (await store.GetTokenByAccessorAsync("acc-1"))!.Accessor);

        await store.DeleteTokenAsync("acc-1");
        Assert.Null(await store.GetTokenByAccessorAsync("acc-1"));
    }

    [Fact]
    public async Task Audit_append_and_read_back_in_order()
    {
        var store = new InMemorySecretStore();
        for (long i = 1; i <= 3; i++)
            await store.AppendAuditAsync(new AuditEntry
            {
                Seq = i, RequestId = $"r{i}", Type = "request", Operation = "read",
            });

        Assert.Equal(3, await store.GetLastAuditSeqAsync());

        var read = new List<long>();
        await foreach (var e in store.ReadAuditAsync(fromSeq: 2)) read.Add(e.Seq);
        Assert.Equal(new long[] { 2, 3 }, read);
    }
}
