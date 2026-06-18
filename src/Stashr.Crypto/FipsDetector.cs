using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Stashr.Crypto;

/// <summary>
/// Authoritative detection of OS FIPS mode (ADR-0004). We never infer FIPS from "crypto ran";
/// we read the OS signal directly. macOS has no first-class FIPS module, so it reports false.
/// </summary>
public static class FipsDetector
{
    /// <summary>True only when the host OS reports an active FIPS-validated cryptographic module.</summary>
    public static bool IsFipsEnabled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return IsWindowsFipsEnabled();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return IsLinuxFipsEnabled();
        return false; // macOS / unknown: treat as non-FIPS (dev only).
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsFipsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Control\Lsa\FipsAlgorithmPolicy");
            return key?.GetValue("Enabled") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLinuxFipsEnabled()
    {
        // Kernel FIPS mode. Necessary signal; the OpenSSL FIPS provider must also be active,
        // which the startup KAT in OsCryptoProvider confirms (ADR-0004).
        try
        {
            const string path = "/proc/sys/crypto/fips_enabled";
            return File.Exists(path) && File.ReadAllText(path).Trim() == "1";
        }
        catch
        {
            return false;
        }
    }
}
