using System.Text.Json;
using Stashr.Core.Cryptography;

namespace Stashr.Engine.Seal;

/// <summary>
/// Config-key auto-unseal (ADR-0012): wraps the master key under a configured 256-bit key with
/// AES-256-GCM. The wrapping key plays the role a cloud KMS/HSM would; for real TPM/HSM, provide
/// a different <see cref="IAutoUnsealProvider"/> backed by the device. Keep the configured key
/// outside the stashr store (e.g. an injected secret / env var).
/// </summary>
public sealed class StaticKeyAutoUnseal(ICryptoProvider crypto, byte[] unsealKey) : IAutoUnsealProvider
{
    public byte[] Wrap(ReadOnlySpan<byte> masterKey)
        => JsonSerializer.SerializeToUtf8Bytes(crypto.Encrypt(masterKey, unsealKey));

    public byte[] Unwrap(byte[] sealedMaster)
        => crypto.Decrypt(JsonSerializer.Deserialize<SealedBlob>(sealedMaster)!, unsealKey);
}
