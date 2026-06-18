using Stashr.Core;
using Stashr.Crypto;
using Stashr.Engine.Seal;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class KeyManagerTests
{
    private static KeyManager New(out InMemorySecretStore store)
    {
        store = new InMemorySecretStore();
        return new KeyManager(store, new OsCryptoProvider());
    }

    [Fact]
    public async Task Initialize_returns_shares_and_leaves_engine_unsealed()
    {
        using var km = New(out _);
        await km.LoadStateAsync();
        Assert.False(km.IsInitialized);

        var init = await km.InitializeAsync(totalShares: 5, threshold: 3);

        Assert.Equal(5, init.Shares.Count);
        Assert.True(km.IsInitialized);
        Assert.False(km.IsSealed);
    }

    [Fact]
    public async Task Encrypt_then_decrypt_round_trips_while_unsealed()
    {
        using var km = New(out _);
        await km.InitializeAsync(5, 3);

        var blob = km.EncryptValue("connection-string"u8);
        Assert.Equal("connection-string"u8.ToArray(), km.DecryptValue(blob));
    }

    [Fact]
    public async Task Seal_then_unseal_with_threshold_recovers_the_same_data_key()
    {
        using var km = New(out _);
        var init = await km.InitializeAsync(5, 3);
        var blob = km.EncryptValue("secret"u8);

        km.Seal();
        Assert.True(km.IsSealed);
        Assert.Throws<SealedException>(() => km.DecryptValue(blob));

        var s1 = await km.SubmitUnsealShareAsync(init.Shares[0]);
        Assert.True(s1.Sealed);
        Assert.Equal(1, s1.Progress);

        await km.SubmitUnsealShareAsync(init.Shares[2]);
        var s3 = await km.SubmitUnsealShareAsync(init.Shares[4]);

        Assert.False(s3.Sealed);
        // Same DEK recovered → the value sealed before sealing still decrypts.
        Assert.Equal("secret"u8.ToArray(), km.DecryptValue(blob));
    }

    [Fact]
    public async Task Unseal_with_corrupted_share_fails()
    {
        using var km = New(out _);
        var init = await km.InitializeAsync(5, 3);
        km.Seal();

        // Corrupt the last byte of one valid share.
        var bytes = Convert.FromHexString(init.Shares[1]);
        bytes[^1] ^= 0xFF;
        var corrupted = Convert.ToHexString(bytes);

        await km.SubmitUnsealShareAsync(init.Shares[0]);
        await km.SubmitUnsealShareAsync(init.Shares[2]);
        await Assert.ThrowsAsync<UnsealFailedException>(() => km.SubmitUnsealShareAsync(corrupted));
        Assert.True(km.IsSealed); // still sealed after a failed attempt
    }

    [Fact]
    public async Task Double_initialize_is_rejected()
    {
        using var km = New(out _);
        await km.InitializeAsync(5, 3);
        await Assert.ThrowsAsync<StashrException>(() => km.InitializeAsync(5, 3));
    }
}
