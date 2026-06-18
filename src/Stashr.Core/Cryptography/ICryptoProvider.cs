namespace Stashr.Core.Cryptography;

/// <summary>
/// The single seam through which all secret-grade cryptography flows (ADR-0004).
/// Implementations route to OS-provided FIPS-validated modules in production and to
/// standard .NET crypto in development. No other code calls primitives directly.
/// </summary>
public interface ICryptoProvider
{
    /// <summary>True only when a FIPS-validated module is confirmed active (ADR-0004).</summary>
    bool FipsMode { get; }

    /// <summary>A human-readable description of the active backend (for startup logging).</summary>
    string BackendDescription { get; }

    /// <summary>Fill <paramref name="destination"/> with cryptographically secure random bytes.</summary>
    void GetRandomBytes(Span<byte> destination);

    /// <summary>
    /// AEAD encrypt. Produces a fresh random nonce and an authentication tag.
    /// The returned ciphertext layout is implementation-defined but self-describing
    /// (see <see cref="SealedBlob"/>).
    /// </summary>
    SealedBlob Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);

    /// <summary>AEAD decrypt a blob produced by <see cref="Encrypt"/>. Throws on auth failure.</summary>
    byte[] Decrypt(SealedBlob blob, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);

    /// <summary>Compute HMAC-SHA-256 of <paramref name="data"/> under <paramref name="key"/>.</summary>
    byte[] Hmac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key);

    /// <summary>Compute SHA-256 of <paramref name="data"/>.</summary>
    byte[] Sha256(ReadOnlySpan<byte> data);

    /// <summary>Run the startup known-answer self-test; throws if the backend misbehaves (ADR-0004).</summary>
    void SelfTest();
}
