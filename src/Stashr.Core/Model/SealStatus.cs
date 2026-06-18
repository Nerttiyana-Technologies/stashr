namespace Stashr.Core.Model;

/// <summary>The engine's seal/HA state, surfaced on the health/seal-status endpoints (ADR-0002).</summary>
public sealed record SealStatus
{
    public required bool Initialized { get; init; }
    public required bool Sealed { get; init; }

    /// <summary>Shamir threshold (M) required to unseal.</summary>
    public int Threshold { get; init; }

    /// <summary>Total shares generated (N).</summary>
    public int TotalShares { get; init; }

    /// <summary>Shares submitted so far in the current unseal attempt.</summary>
    public int Progress { get; init; }

    /// <summary>"active" | "standby" | "sealed" (ADR-0002).</summary>
    public string HaRole { get; init; } = "sealed";
}
