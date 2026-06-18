namespace Stashr.Core.Model;

/// <summary>Service tokens are tracked/revocable; batch tokens are stateless (ADR-0006).</summary>
public enum TokenType
{
    Service,
    Batch,
}

/// <summary>
/// Server-side record of a token. The raw token value is never stored; only its HMAC
/// and the accessor handle used for audit/revocation (ADR-0005/0006).
/// </summary>
public sealed record TokenInfo
{
    public required string Accessor { get; init; }
    public required byte[] TokenHmac { get; init; }
    public required IReadOnlyList<string> Policies { get; init; }
    public TokenType Type { get; init; } = TokenType.Service;
    public string? Parent { get; init; }
    public int NumUses { get; init; }
    public TimeSpan Ttl { get; init; }
    public TimeSpan MaxTtl { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlyList<string> BoundCidrs { get; init; } = Array.Empty<string>();
}
