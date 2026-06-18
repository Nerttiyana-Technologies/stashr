namespace Stashr.Core.Cryptography;

/// <summary>
/// A self-describing AEAD ciphertext: the random nonce, the ciphertext, and the auth tag.
/// Persisted alongside the wrapped data key and a key-version reference (ADR-0004, ADR-0007).
/// </summary>
public sealed record SealedBlob
{
    public required byte[] Nonce { get; init; }
    public required byte[] Ciphertext { get; init; }
    public required byte[] Tag { get; init; }

    /// <summary>Algorithm marker for forward-compatibility (e.g. "AES-256-GCM").</summary>
    public string Algorithm { get; init; } = "AES-256-GCM";
}
