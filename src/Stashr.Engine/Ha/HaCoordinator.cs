using Stashr.Core.Ha;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Ha;

/// <summary>
/// Drives the active/standby state machine (ADR-0002). Each tick: a standby tries to acquire
/// leadership; a leader checks it still holds it and, if not, <b>fail-closed self-seals</b>
/// (zeroes its keys) so a partitioned former leader cannot keep serving. Fencing is provided by
/// the election's monotonic <see cref="Epoch"/>. Full request-forwarding between nodes and true
/// network-partition handling are deployment concerns layered on top of this.
/// </summary>
public sealed class HaCoordinator(ILeaderElection election, KeyManager keys)
{
    public bool IsLeader { get; private set; }
    public long Epoch { get; private set; }
    public string Role => IsLeader ? "active" : "standby";

    /// <summary>One step of the state machine. Drive on a timer (host) or directly (tests).</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        if (!IsLeader)
        {
            if (await election.TryAcquireAsync(ct))
            {
                IsLeader = true;
                Epoch = election.Epoch;
            }
        }
        else if (!await election.IsHeldAsync(ct))
        {
            IsLeader = false;
            keys.Seal(); // lost leadership → fail-closed (ADR-0002)
        }
    }
}
