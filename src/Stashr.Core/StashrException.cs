namespace Stashr.Core;

/// <summary>Base type for stashr domain errors that map to API problem-details responses.</summary>
public class StashrException : Exception
{
    public StashrException(string message) : base(message) { }
    public StashrException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when an operation is attempted while the engine is sealed (ADR-0002).</summary>
public sealed class SealedException() : StashrException("stashr is sealed");

/// <summary>Thrown when authorization is denied by policy (ADR-0008).</summary>
public sealed class PermissionDeniedException(string path)
    : StashrException($"permission denied for path '{path}'");

/// <summary>Thrown on optimistic-concurrency (CAS) mismatch for a KV write (ADR-0010).</summary>
public sealed class CasMismatchException(int expected, int actual)
    : StashrException($"check-and-set mismatch: expected version {expected}, found {actual}");
