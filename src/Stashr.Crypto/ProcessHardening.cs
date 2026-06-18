using System.Runtime.InteropServices;

namespace Stashr.Crypto;

/// <summary>
/// Process-level hardening for secret material (ADR-0015). A crash/core dump captures the whole
/// heap — including the master key — so the engine disables core dumps at startup. Best-effort
/// and platform-specific: Linux uses <c>prctl(PR_SET_DUMPABLE,0)</c> + <c>setrlimit</c>; macOS uses
/// <c>setrlimit</c>; Windows is a no-op here (handle via WER policy in deployment).
/// </summary>
public static class ProcessHardening
{
    private const int PR_SET_DUMPABLE = 4;
    private const int RLIMIT_CORE = 4; // same value on Linux and macOS/BSD

    [StructLayout(LayoutKind.Sequential)]
    private struct Rlimit
    {
        public ulong Cur;
        public ulong Max;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    [DllImport("libc", SetLastError = true)]
    private static extern int setrlimit(int resource, ref Rlimit rlim);

    /// <summary>Disable core dumps for this process. Returns true if a hardening call was applied.</summary>
    public static bool TryDisableCoreDumps()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                prctl(PR_SET_DUMPABLE, 0, 0, 0, 0);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var rlim = new Rlimit { Cur = 0, Max = 0 };
                setrlimit(RLIMIT_CORE, ref rlim);
                return true;
            }

            return false; // Windows: rely on WER configuration
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
