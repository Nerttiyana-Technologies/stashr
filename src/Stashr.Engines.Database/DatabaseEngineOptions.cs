namespace Stashr.Engines.Database;

/// <summary>
/// Configuration for the database secrets engine (ADR-0009). The admin connection and role
/// templates come from host configuration. SQL templates use <c>{{name}}</c>, <c>{{password}}</c>
/// and <c>{{expiration}}</c> placeholders (values are server-generated, so safe to interpolate).
/// </summary>
public sealed class DatabaseEngineOptions
{
    /// <summary>Privileged connection used to create and drop the dynamic roles.</summary>
    public string AdminConnectionString { get; set; } = string.Empty;

    /// <summary>Named roles a caller can request credentials for.</summary>
    public Dictionary<string, DatabaseRole> Roles { get; set; } = new();
}

public sealed class DatabaseRole
{
    /// <summary>SQL run to create the dynamic role (default: a LOGIN role valid until expiry).</summary>
    public string CreationStatements { get; set; } = string.Empty;

    /// <summary>SQL run to revoke it (default: terminate sessions + DROP ROLE).</summary>
    public string RevocationStatements { get; set; } = string.Empty;

    public int DefaultTtlSeconds { get; set; } = 3600;
}
