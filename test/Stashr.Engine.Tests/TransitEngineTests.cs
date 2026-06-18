using System.Text;
using Stashr.Core.Engines;
using Stashr.Crypto;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class TransitEngineTests
{
    private static async Task<StashrEngine> UnsealedAsync()
    {
        var engine = new StashrEngine(new InMemorySecretStore(), new OsCryptoProvider());
        await engine.Keys.InitializeAsync(5, 3);
        return engine;
    }

    [Fact]
    public async Task Encrypt_then_decrypt_round_trips()
    {
        var engine = await UnsealedAsync();
        await engine.Router.RouteAsync(EngineOp.Write, "transit/keys/orders", null);

        var plaintext = Convert.ToBase64String("card-number-4111"u8.ToArray());

        var enc = await engine.Router.RouteAsync(EngineOp.Write, "transit/encrypt/orders",
            new Dictionary<string, string> { ["plaintext"] = plaintext });
        var ciphertext = enc.Data!["ciphertext"];
        Assert.StartsWith("stashr:v1:", ciphertext);

        var dec = await engine.Router.RouteAsync(EngineOp.Write, "transit/decrypt/orders",
            new Dictionary<string, string> { ["ciphertext"] = ciphertext });

        Assert.Equal(plaintext, dec.Data!["plaintext"]);
        Assert.Equal("card-number-4111", Encoding.UTF8.GetString(Convert.FromBase64String(dec.Data["plaintext"])));
    }

    [Fact]
    public async Task Each_encryption_is_unique_even_for_the_same_plaintext()
    {
        var engine = await UnsealedAsync();
        await engine.Router.RouteAsync(EngineOp.Write, "transit/keys/k", null);
        var pt = Convert.ToBase64String("same"u8.ToArray());

        var a = (await engine.Router.RouteAsync(EngineOp.Write, "transit/encrypt/k",
            new Dictionary<string, string> { ["plaintext"] = pt })).Data!["ciphertext"];
        var b = (await engine.Router.RouteAsync(EngineOp.Write, "transit/encrypt/k",
            new Dictionary<string, string> { ["plaintext"] = pt })).Data!["ciphertext"];

        Assert.NotEqual(a, b); // fresh nonce each time
    }

    [Fact]
    public async Task Encrypt_with_unknown_key_is_missing()
    {
        var engine = await UnsealedAsync();
        var r = await engine.Router.RouteAsync(EngineOp.Write, "transit/encrypt/nope",
            new Dictionary<string, string> { ["plaintext"] = Convert.ToBase64String("x"u8.ToArray()) });
        Assert.True(r.NotFound);
    }

    [Fact]
    public async Task Key_creation_is_idempotent_and_keeps_data_decryptable()
    {
        var engine = await UnsealedAsync();
        await engine.Router.RouteAsync(EngineOp.Write, "transit/keys/k", null);
        var pt = Convert.ToBase64String("v"u8.ToArray());
        var ct = (await engine.Router.RouteAsync(EngineOp.Write, "transit/encrypt/k",
            new Dictionary<string, string> { ["plaintext"] = pt })).Data!["ciphertext"];

        // Re-create must not overwrite the key (would break existing ciphertext).
        await engine.Router.RouteAsync(EngineOp.Write, "transit/keys/k", null);

        var dec = await engine.Router.RouteAsync(EngineOp.Write, "transit/decrypt/k",
            new Dictionary<string, string> { ["ciphertext"] = ct });
        Assert.Equal(pt, dec.Data!["plaintext"]);
    }
}
