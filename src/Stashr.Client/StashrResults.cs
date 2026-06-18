namespace Stashr.Client;

/// <summary>Seal/HA status returned by the server.</summary>
public sealed class SealStatusInfo
{
    public bool Initialized { get; set; }
    public bool Sealed { get; set; }
    public int Threshold { get; set; }
    public int TotalShares { get; set; }
    public int Progress { get; set; }
}

/// <summary>Result of initializing the engine: the one-time shares and root token.</summary>
public sealed class InitInfo
{
    public IReadOnlyList<string> Keys { get; set; } = Array.Empty<string>();
    public string RootToken { get; set; } = string.Empty;
}

/// <summary>Result of verifying the audit hash-chain.</summary>
public sealed class AuditVerifyInfo
{
    public bool Valid { get; set; }
    public long? FirstBrokenSeq { get; set; }
    public long Checked { get; set; }
}

/// <summary>Result of an "explain access" query.</summary>
public sealed class ExplainInfo
{
    public bool Allowed { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
