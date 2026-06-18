namespace Stashr.Core.Model;

/// <summary>Capabilities a policy rule may grant on a path (ADR-0008).</summary>
[Flags]
public enum Capability
{
    None    = 0,
    Create  = 1 << 0,
    Read    = 1 << 1,
    Update  = 1 << 2,
    Delete  = 1 << 3,
    List    = 1 << 4,
    Sudo    = 1 << 5,
    /// <summary>Explicit deny — overrides any allow at equal specificity (ADR-0008).</summary>
    Deny    = 1 << 6,
}

/// <summary>A single path-pattern → capabilities grant.</summary>
public sealed record PolicyRule
{
    public required string PathPattern { get; init; }
    public required Capability Capabilities { get; init; }

    /// <summary>True when the pattern ends in a glob ("*"), affecting precedence (ADR-0008).</summary>
    public bool IsGlob => PathPattern.EndsWith("*", StringComparison.Ordinal);
}

/// <summary>A named, versioned policy: deny-by-default set of rules (ADR-0008).</summary>
public sealed record Policy
{
    public required string Name { get; init; }
    public required IReadOnlyList<PolicyRule> Rules { get; init; }
    public int Version { get; init; } = 1;
}
