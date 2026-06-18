namespace Stashr.Core.Ha;

/// <summary>
/// Single-leader election for active/standby HA (ADR-0002). Backed by a database advisory lock:
/// only one node holds leadership at a time; on release/failure another can acquire it. Each
/// acquisition advances a monotonic <see cref="Epoch"/> (the fencing token) so a stale former
/// leader can be detected.
/// </summary>
public interface ILeaderElection
{
    /// <summary>The fencing epoch of the current leadership (monotonically increasing).</summary>
    long Epoch { get; }

    /// <summary>Try to become leader. Returns true if leadership was acquired (or already held).</summary>
    Task<bool> TryAcquireAsync(CancellationToken ct = default);

    /// <summary>Verify leadership is still held (heartbeat). False if it was lost.</summary>
    Task<bool> IsHeldAsync(CancellationToken ct = default);

    /// <summary>Relinquish leadership.</summary>
    Task ReleaseAsync(CancellationToken ct = default);
}
