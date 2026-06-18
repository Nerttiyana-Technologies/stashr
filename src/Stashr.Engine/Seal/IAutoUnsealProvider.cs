namespace Stashr.Engine.Seal;

/// <summary>
/// Wraps/unwraps the root master key for auto-unseal (ADR-0012). The config-key provider uses a
/// configured key; TPM/HSM/cloud-KMS providers implement the same contract against their device.
/// With auto-unseal, the engine recovers the master key at startup without operator shares;
/// Shamir <i>recovery</i> keys are still retained for dual-control rekey/recovery.
/// </summary>
public interface IAutoUnsealProvider
{
    byte[] Wrap(ReadOnlySpan<byte> masterKey);
    byte[] Unwrap(byte[] sealedMaster);
}
