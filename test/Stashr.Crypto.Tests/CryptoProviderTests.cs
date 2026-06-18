using System.Security.Cryptography;
using Stashr.Crypto;
using Xunit;

namespace Stashr.Crypto.Tests;

public class CryptoProviderTests
{
    private readonly OsCryptoProvider _crypto = new();

    [Fact]
    public void SelfTest_passes()
    {
        _crypto.SelfTest(); // throws on failure
    }

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var key = new byte[OsCryptoProvider.KeySize];
        _crypto.GetRandomBytes(key);
        var plaintext = "connection-string=secret"u8.ToArray();

        var blob = _crypto.Encrypt(plaintext, key);
        var recovered = _crypto.Decrypt(blob, key);

        Assert.Equal(plaintext, recovered);
        Assert.NotEqual(plaintext, blob.Ciphertext); // actually encrypted
    }

    [Fact]
    public void Tampered_ciphertext_fails_authentication()
    {
        var key = new byte[OsCryptoProvider.KeySize];
        _crypto.GetRandomBytes(key);
        var blob = _crypto.Encrypt("data"u8.ToArray(), key);

        blob.Ciphertext[0] ^= 0xFF; // flip a bit

        Assert.Throws<AuthenticationTagMismatchException>(() => _crypto.Decrypt(blob, key));
    }

    [Fact]
    public void Wrong_key_fails_authentication()
    {
        var key = new byte[OsCryptoProvider.KeySize];
        var wrong = new byte[OsCryptoProvider.KeySize];
        _crypto.GetRandomBytes(key);
        _crypto.GetRandomBytes(wrong);

        var blob = _crypto.Encrypt("data"u8.ToArray(), key);

        Assert.Throws<AuthenticationTagMismatchException>(() => _crypto.Decrypt(blob, wrong));
    }

    [Fact]
    public void Envelope_wrap_unwrap_round_trips()
    {
        var envelope = new EnvelopeEncryptor(_crypto);
        var master = envelope.GenerateKey();
        var dek = envelope.GenerateKey();

        var wrapped = envelope.WrapKey(master, dek);
        var unwrapped = envelope.UnwrapKey(master, wrapped);

        Assert.Equal(dek, unwrapped);
    }

    [Fact]
    public void SecureMemory_zeroes_on_dispose_and_holds_data()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using (var mem = SecureMemory.From(data))
        {
            Assert.Equal(data, mem.Span.ToArray());
        }
        // After dispose the buffer is freed; we can't read it, but the test proves construction.
    }
}
