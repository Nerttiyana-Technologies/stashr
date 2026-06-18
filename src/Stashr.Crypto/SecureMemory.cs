using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Stashr.Crypto;

/// <summary>
/// A page-locked, zero-on-dispose buffer for secret material (ADR-0015). Backed by unmanaged
/// memory so the GC never relocates or copies it, and locked into RAM (best-effort) so it is
/// not swapped to disk. Access secrets only through <see cref="Span"/>; never copy them into a
/// <see cref="string"/>.
/// </summary>
public sealed unsafe class SecureMemory : IDisposable
{
    private byte* _ptr;
    private readonly int _length;
    private bool _locked;
    private bool _disposed;

    public SecureMemory(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        _length = length;
        _ptr = (byte*)NativeMemory.AllocZeroed((nuint)length);
        _locked = TryLock(_ptr, (nuint)length);
    }

    /// <summary>Allocate and copy <paramref name="source"/> in, then the caller should clear its own copy.</summary>
    public static SecureMemory From(ReadOnlySpan<byte> source)
    {
        var mem = new SecureMemory(source.Length);
        source.CopyTo(mem.Span);
        return mem;
    }

    public int Length => _length;

    /// <summary>True if the buffer was successfully locked into RAM (mlock/VirtualLock).</summary>
    public bool IsLocked => _locked;

    public Span<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new Span<byte>(_ptr, _length);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ptr is not null)
        {
            CryptographicOperations.ZeroMemory(new Span<byte>(_ptr, _length));
            if (_locked) TryUnlock(_ptr, (nuint)_length);
            NativeMemory.Free(_ptr);
            _ptr = null;
        }
    }

    // --- best-effort page locking (ADR-0015). Failures are non-fatal: we degrade to unlocked. ---

    private static bool TryLock(void* addr, nuint len)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return VirtualLock((nint)addr, len);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return mlock((nint)addr, len) == 0;
        }
        catch (DllNotFoundException) { /* platform without the symbol: degrade */ }
        catch (EntryPointNotFoundException) { }
        return false;
    }

    private static void TryUnlock(void* addr, nuint len)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) VirtualUnlock((nint)addr, len);
            else munlock((nint)addr, len);
        }
        catch { /* best effort */ }
    }

    [DllImport("libc", SetLastError = true)] private static extern int mlock(nint addr, nuint len);
    [DllImport("libc", SetLastError = true)] private static extern int munlock(nint addr, nuint len);

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualLock(nint addr, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualUnlock(nint addr, nuint size);
}
