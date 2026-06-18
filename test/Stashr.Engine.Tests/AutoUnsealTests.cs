using System.Security.Cryptography;
using Stashr.Crypto;
using Stashr.Engine.Seal;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class AutoUnsealTests
{
    private static byte[] NewKey()
    {
        var k = new byte[OsCryptoProvider.KeySize];
        RandomNumberGenerator.Fill(k);
        return k;
    }

    [Fact]
    public async Task Auto_unseal_recovers_without_operator_shares()
    {
        var store = new InMemorySecretStore();
        var crypto = new OsCryptoProvider();
        using var km = new KeyManager(store, crypto, new StaticKeyAutoUnseal(crypto, NewKey()));

        await km.InitializeAsync(5, 3);
        var blob = km.EncryptValue("secret"u8);

        km.Seal();
        Assert.True(km.IsSealed);

        Assert.True(await km.TryAutoUnsealAsync()); // no shares submitted
        Assert.False(km.IsSealed);
        Assert.Equal("secret"u8.ToArray(), km.DecryptValue(blob));
    }

    [Fact]
    public async Task Recovery_shares_still_unseal_in_auto_mode()
    {
        var store = new InMemorySecretStore();
        var crypto = new OsCryptoProvider();
        using var km = new KeyManager(store, crypto, new StaticKeyAutoUnseal(crypto, NewKey()));

        var init = await km.InitializeAsync(5, 3);
        km.Seal();

        await km.SubmitUnsealShareAsync(init.Shares[0]);
        await km.SubmitUnsealShareAsync(init.Shares[1]);
        var s = await km.SubmitUnsealShareAsync(init.Shares[2]);
        Assert.False(s.Sealed); // recovery keys provide dual-control fallback
    }

    [Fact]
    public async Task No_provider_means_no_auto_unseal()
    {
        var store = new InMemorySecretStore();
        using var km = new KeyManager(store, new OsCryptoProvider());
        await km.InitializeAsync(5, 3);
        km.Seal();

        Assert.False(await km.TryAutoUnsealAsync());
        Assert.True(km.IsSealed);
    }

    [Fact]
    public async Task Wrong_unseal_key_cannot_auto_unseal()
    {
        var store = new InMemorySecretStore();
        var crypto = new OsCryptoProvider();

        using (var kmA = new KeyManager(store, crypto, new StaticKeyAutoUnseal(crypto, NewKey())))
            await kmA.InitializeAsync(5, 3); // master wrapped under key A

        using var kmB = new KeyManager(store, crypto, new StaticKeyAutoUnseal(crypto, NewKey())); // different key
        await kmB.LoadStateAsync();
        Assert.False(await kmB.TryAutoUnsealAsync());
        Assert.True(kmB.IsSealed);
    }
}
