namespace Stashr.Core.Model;

/// <summary>
/// One audit record. Secret values and tokens are referenced only by HMAC, never in
/// plaintext (ADR-0005). <see cref="ChainHash"/> links it to the prior entry for
/// tamper-evidence.
/// </summary>
public sealed record AuditEntry
{
    public required long Seq { get; init; }
    public DateTimeOffset Time { get; init; }
    public required string RequestId { get; init; }
    public required string Type { get; init; }        // "request" | "response"
    public required string Operation { get; init; }   // read|write|list|delete|login|unseal|...
    public string? Path { get; init; }
    public string? TokenAccessor { get; init; }
    public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();
    public string? RemoteAddr { get; init; }
    public string Decision { get; init; } = "granted"; // granted|denied
    public string? Error { get; init; }
    public string? ValueHmac { get; init; }            // present when an event concerns a value
    public string? Node { get; init; }

    /// <summary>HMAC(chainKey, prevHash || canonical(entry)) — see ADR-0005.</summary>
    public string? ChainHash { get; init; }
}
