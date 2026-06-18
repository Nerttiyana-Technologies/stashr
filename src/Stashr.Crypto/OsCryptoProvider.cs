using System.Security.Cryptography;
using Stashr.Core.Cryptography;

namespace Stashr.Crypto;

/// <summary>
/// The production crypto provider (ADR-0004). All operations use the .NET BCL types that
/// delegate to the OS module; on a FIPS-configured host these resolve to the validated
/// Windows CNG / Linux OpenSSL FIPS provider. No third-party crypto on this path.
/// </summary>
public sealed class OsCryptoProvider : ICryptoProvider
{
    public const int KeySize = 32;   // AES-256
    public const int NonceSize = 12; // GCM standard nonce
    public const int TagSize = 16;   // 128-bit tag

    public bool FipsMode { get; } = FipsDetector.IsFipsEnabled();

    public string BackendDescription =>
        FipsMode
            ? "OS-provided FIPS-validated module (CNG / OpenSSL FIPS provider)"
            : "OS-provided standard cryptography (NOT FIPS — development only)";

    public void GetRandomBytes(Span<byte> destination) => RandomNumberGenerator.Fill(destination);

    public SealedBlob Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new SealedBlob { Nonce = nonce, Ciphertext = ciphertext, Tag = tag };
    }

    public byte[] Decrypt(SealedBlob blob, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));

        var plaintext = new byte[blob.Ciphertext.Length];
        using var gcm = new AesGcm(key, blob.Tag.Length);
        gcm.Decrypt(blob.Nonce, blob.Ciphertext, blob.Tag, plaintext, associatedData);
        return plaintext;
    }

    public byte[] Hmac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key) => HMACSHA256.HashData(key, data);

    public byte[] Sha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    /// <summary>Known-answer round-trip; confirms the active module actually works (ADR-0004).</summary>
    public void SelfTest()
    {
        Span<byte> key = stackalloc byte[KeySize];
        GetRandomBytes(key);
        var plaintext = "stashr-self-test"u8.ToArray();

        var blob = Encrypt(plaintext, key);
        var roundTrip = Decrypt(blob, key);

        if (!CryptographicOperations.FixedTimeEquals(plaintext, roundTrip))
            throw new InvalidOperationException("crypto provider self-test failed: AEAD round-trip mismatch");
    }
}
