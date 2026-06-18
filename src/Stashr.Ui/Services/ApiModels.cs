using System.Text.Json.Serialization;

namespace Stashr.Ui.Services;

// Response shapes mirror the Vault-compatible /v1 API (see Stashr.Server).

public sealed class HealthDto
{
    [JsonPropertyName("initialized")] public bool Initialized { get; set; }
    [JsonPropertyName("sealed")] public bool Sealed { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

public sealed class SealStatusDto
{
    [JsonPropertyName("initialized")] public bool Initialized { get; set; }
    [JsonPropertyName("sealed")] public bool Sealed { get; set; }
    [JsonPropertyName("t")] public int Threshold { get; set; }
    [JsonPropertyName("n")] public int TotalShares { get; set; }
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("ha_role")] public string HaRole { get; set; } = "";
}

public sealed class InitResultDto
{
    [JsonPropertyName("keys")] public List<string> Keys { get; set; } = new();
    [JsonPropertyName("secret_shares")] public int SecretShares { get; set; }
    [JsonPropertyName("secret_threshold")] public int SecretThreshold { get; set; }
    [JsonPropertyName("root_token")] public string RootToken { get; set; } = "";
}

// --- KV v2 ---

public sealed class KvReadEnvelope
{
    [JsonPropertyName("data")] public KvReadData? Data { get; set; }
}
public sealed class KvReadData
{
    [JsonPropertyName("data")] public Dictionary<string, string> Data { get; set; } = new();
    [JsonPropertyName("metadata")] public KvReadMeta? Metadata { get; set; }
}
public sealed class KvReadMeta
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("created_time")] public DateTimeOffset CreatedTime { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
    [JsonPropertyName("destroyed")] public bool Destroyed { get; set; }
}

public sealed class KeysEnvelope
{
    [JsonPropertyName("data")] public KeysData? Data { get; set; }
}
public sealed class KeysData
{
    [JsonPropertyName("keys")] public List<string> Keys { get; set; } = new();
}

// --- policies ---

public sealed class PolicyEnvelope
{
    [JsonPropertyName("data")] public PolicyDto? Data { get; set; }
}
public sealed class PolicyDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("rules")] public List<PolicyRuleDto> Rules { get; set; } = new();
}
public sealed class PolicyRuleDto
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = new();
}

public sealed class ExplainDto
{
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("capability")] public string Capability { get; set; } = "";
    [JsonPropertyName("winning_policy")] public string? WinningPolicy { get; set; }
    [JsonPropertyName("winning_rule")] public string? WinningRule { get; set; }
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
}

// --- audit ---

public sealed class AuditVerifyDto
{
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("first_broken_seq")] public long? FirstBrokenSeq { get; set; }
    [JsonPropertyName("checked_count")] public int CheckedCount { get; set; }
}

// --- access (AppRole) ---

public sealed class RoleIdEnvelope
{
    [JsonPropertyName("data")] public RoleIdData? Data { get; set; }
}
public sealed class RoleIdData
{
    [JsonPropertyName("role_id")] public string RoleId { get; set; } = "";
}
public sealed class SecretIdEnvelope
{
    [JsonPropertyName("data")] public SecretIdData? Data { get; set; }
    [JsonPropertyName("wrap_info")] public WrapInfo? WrapInfo { get; set; }
}
public sealed class SecretIdData
{
    [JsonPropertyName("secret_id")] public string SecretId { get; set; } = "";
}
public sealed class WrapInfo
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("ttl")] public int Ttl { get; set; }
}

public sealed class AuthEnvelope
{
    [JsonPropertyName("auth")] public AuthData? Auth { get; set; }
}
public sealed class AuthData
{
    [JsonPropertyName("client_token")] public string ClientToken { get; set; } = "";
    [JsonPropertyName("accessor")] public string? Accessor { get; set; }
    [JsonPropertyName("policies")] public List<string> Policies { get; set; } = new();
}

// --- transit / mounts (generic data bag) ---

public sealed class DataEnvelope
{
    [JsonPropertyName("data")] public Dictionary<string, string>? Data { get; set; }
}

public sealed class MountsEnvelope
{
    [JsonPropertyName("data")] public MountsData? Data { get; set; }
}
public sealed class MountsData
{
    [JsonPropertyName("mounts")] public List<MountDto> Mounts { get; set; } = new();
}
public sealed class MountDto
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}
