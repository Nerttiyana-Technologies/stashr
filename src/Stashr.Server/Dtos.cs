using System.Text.Json.Serialization;

namespace Stashr.Server;

// --- request bodies (Vault-compatible field names, ADR-0003) ---

public sealed record InitRequest
{
    [JsonPropertyName("secret_shares")] public int? SecretShares { get; init; }
    [JsonPropertyName("secret_threshold")] public int? SecretThreshold { get; init; }
}

public sealed record UnsealRequest
{
    [JsonPropertyName("key")] public string? Key { get; init; }
}

public sealed record KvWriteRequest
{
    [JsonPropertyName("data")] public Dictionary<string, string>? Data { get; init; }
    [JsonPropertyName("options")] public KvOptions? Options { get; init; }
}

public sealed record KvOptions
{
    [JsonPropertyName("cas")] public int? Cas { get; init; }
}

public sealed record PolicyWriteRequest
{
    [JsonPropertyName("rules")] public List<PolicyRuleDto>? Rules { get; init; }
}

public sealed record PolicyRuleDto
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; init; }
}

public sealed record ExplainRequest
{
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("capability")] public string? Capability { get; init; }
    [JsonPropertyName("policies")] public List<string>? Policies { get; init; }
}

public sealed record WrapRequest
{
    [JsonPropertyName("data")] public Dictionary<string, string>? Data { get; init; }
    [JsonPropertyName("ttl")] public int? TtlSeconds { get; init; }
}

public sealed record UnwrapRequest
{
    [JsonPropertyName("token")] public string? Token { get; init; }
}

public sealed record AppRoleCreateRequest
{
    [JsonPropertyName("token_policies")] public List<string>? TokenPolicies { get; init; }
    [JsonPropertyName("token_ttl")] public int? TokenTtl { get; init; }
    [JsonPropertyName("secret_id_ttl")] public int? SecretIdTtl { get; init; }
    [JsonPropertyName("secret_id_num_uses")] public int? SecretIdNumUses { get; init; }
}

public sealed record SecretIdRequest
{
    [JsonPropertyName("wrap_ttl")] public int? WrapTtl { get; init; }
}

public sealed record AppRoleLoginRequest
{
    [JsonPropertyName("role_id")] public string? RoleId { get; init; }
    [JsonPropertyName("secret_id")] public string? SecretId { get; init; }
}

public sealed record TransitEncryptRequest
{
    [JsonPropertyName("plaintext")] public string? Plaintext { get; init; } // base64
}

public sealed record TransitDecryptRequest
{
    [JsonPropertyName("ciphertext")] public string? Ciphertext { get; init; }
}

public sealed record LeaseRevokeRequest
{
    [JsonPropertyName("lease_id")] public string? LeaseId { get; init; }
}

public sealed record EntityCreateRequest
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("policies")] public List<string>? Policies { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record GroupCreateRequest
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("policies")] public List<string>? Policies { get; init; }
    [JsonPropertyName("member_entity_ids")] public List<string>? MemberEntityIds { get; init; }
}

public sealed record AliasCreateRequest
{
    [JsonPropertyName("principal")] public string? Principal { get; init; }
    [JsonPropertyName("entity_id")] public string? EntityId { get; init; }
}

// --- response DTO mapped from the domain SealStatus via Synthetix ---

public sealed class SealStatusDto
{
    [JsonPropertyName("initialized")] public bool Initialized { get; set; }
    [JsonPropertyName("sealed")] public bool Sealed { get; set; }
    [JsonPropertyName("t")] public int Threshold { get; set; }
    [JsonPropertyName("n")] public int TotalShares { get; set; }
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("ha_role")] public string HaRole { get; set; } = "";
}
