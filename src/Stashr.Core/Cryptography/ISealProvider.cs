namespace Stashr.Core.Cryptography;

/// <summary>How the engine protects and recovers the root master key (ADR-0012).</summary>
public enum SealMode
{
    /// <summary>M-of-N Shamir shares submitted by operators.</summary>
    Shamir,

    /// <summary>Auto-unseal via TPM/HSM/Transit; recovery keys retained.</summary>
    AutoUnseal,
}

/// <summary>
/// Mediates unseal: wraps the master key into a sealed blob and recovers it at startup.
/// The rest of the engine is unchanged regardless of provider (ADR-0012).
/// </summary>
public interface ISealProvider
{
    SealMode Mode { get; }

    /// <summary>True if the provider's backing module is FIPS-validated (ADR-0004/0012).</summary>
    bool IsFipsBacked { get; }
}
