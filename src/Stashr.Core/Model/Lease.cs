namespace Stashr.Core.Model;

/// <summary>A bounded-lifetime grant tracked by the expiration manager (ADR-0006).</summary>
public sealed record Lease
{
    public required string LeaseId { get; init; }
    public string? Parent { get; init; }
    public DateTimeOffset IssueTime { get; init; }
    public TimeSpan Ttl { get; init; }
    public TimeSpan MaxTtl { get; init; }
    public bool Renewable { get; init; }

    public DateTimeOffset ExpiresAt => IssueTime + Ttl;
}
