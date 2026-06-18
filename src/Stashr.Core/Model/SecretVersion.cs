namespace Stashr.Core.Model;

/// <summary>
/// One immutable version of a KV secret (ADR-0010). The payload is the decrypted
/// key/value map; persistence layers store only the encrypted form (ADR-0007).
/// </summary>
public sealed record SecretVersion
{
    public required int Version { get; init; }
    public required IReadOnlyDictionary<string, string> Data { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public bool Destroyed { get; init; }

    public bool IsActive => DeletedAt is null && !Destroyed;
}

/// <summary>Per-path KV metadata (ADR-0007/0010).</summary>
public sealed record SecretMetadata
{
    public required string Path { get; init; }
    public int CurrentVersion { get; init; }
    public int MaxVersions { get; init; } = 10;
    public bool CasRequired { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string> CustomMetadata { get; init; }
        = new Dictionary<string, string>();
}
