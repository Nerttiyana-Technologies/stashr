using Stashr.Core.Cryptography;

namespace Stashr.Crypto;

/// <summary>
/// Envelope-encryption primitives (ADR-0004): generate data keys and wrap/unwrap them under a
/// higher-level key. The master→KEK→DEK orchestration lives in the engine's key manager; this
/// type provides the raw operations, always via the validated <see cref="ICryptoProvider"/>.
/// </summary>
public sealed class EnvelopeEncryptor(ICryptoProvider crypto)
{
    private readonly ICryptoProvider _crypto = crypto;

    /// <summary>Generate a fresh 256-bit data/key-encryption key.</summary>
    public byte[] GenerateKey()
    {
        var key = new byte[OsCryptoProvider.KeySize];
        _crypto.GetRandomBytes(key);
        return key;
    }

    /// <summary>Wrap <paramref name="keyToWrap"/> under <paramref name="wrappingKey"/> (AES-256-GCM).</summary>
    public SealedBlob WrapKey(ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> keyToWrap)
        => _crypto.Encrypt(keyToWrap, wrappingKey);

    /// <summary>Recover a wrapped key. Throws on authentication failure.</summary>
    public byte[] UnwrapKey(ReadOnlySpan<byte> wrappingKey, SealedBlob wrapped)
        => _crypto.Decrypt(wrapped, wrappingKey);
}
