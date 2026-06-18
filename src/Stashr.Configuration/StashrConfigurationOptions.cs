namespace Stashr.Configuration;

/// <summary>Options for the <c>AddStashr()</c> configuration provider (ADR-0013).</summary>
public sealed class StashrConfigurationOptions
{
    /// <summary>Base address of the stashr server.</summary>
    public string Address { get; set; } = "http://localhost:8200";

    /// <summary>Auth token (X-Vault-Token).</summary>
    public string? Token { get; set; }

    /// <summary>KV mount name (default "secret").</summary>
    public string Mount { get; set; } = "secret";

    /// <summary>The KV secret paths to load into configuration. Each secret's keys become config keys.</summary>
    public IList<string> Paths { get; } = new List<string>();

    /// <summary>
    /// When false (default), startup fails if stashr is unreachable or a path is missing — there
    /// is deliberately no silent plaintext fallback (ADR-0013). When true, errors are swallowed.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>Fluent helper to add a secret path.</summary>
    public StashrConfigurationOptions AddPath(string path)
    {
        Paths.Add(path);
        return this;
    }
}
