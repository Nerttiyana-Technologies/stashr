using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stashr.Ui.Services;

/// <summary>Typed client over the Vault-compatible /v1 API. Adds the token header per request.</summary>
public sealed class StashrApi
{
    private readonly HttpClient _http;
    private readonly AuthState _auth;

    public StashrApi(HttpClient http, AuthState auth)
    {
        _http = http;
        _auth = auth;
    }

    // ----- lifecycle / seal -----

    public Task<HealthDto> HealthAsync() => GetAsync<HealthDto>("v1/sys/health");
    public Task<SealStatusDto> SealStatusAsync() => GetAsync<SealStatusDto>("v1/sys/seal-status");

    public Task<InitResultDto> InitAsync(int shares, int threshold) =>
        PostAsync<InitResultDto>("v1/sys/init", new { secret_shares = shares, secret_threshold = threshold });

    public Task<SealStatusDto> UnsealAsync(string key) =>
        PostAsync<SealStatusDto>("v1/sys/unseal", new { key });

    public Task SealAsync() => SendAsync(HttpMethod.Post, "v1/sys/seal");

    /// <summary>True if the token is accepted (200 or 403); false on 401.</summary>
    public async Task<bool> ValidateTokenAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "v1/sys/policies");
        req.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        using var resp = await _http.SendAsync(req);
        return resp.StatusCode != HttpStatusCode.Unauthorized;
    }

    // ----- KV v2 -----

    public async Task<IReadOnlyList<string>> KvListAsync(string path)
    {
        var env = await GetAsync<KeysEnvelope>($"v1/secret/metadata/{path}?list=true");
        return env.Data?.Keys ?? new List<string>();
    }

    public async Task<KvReadData?> KvReadAsync(string path, int? version = null)
    {
        var url = $"v1/secret/data/{path}" + (version is { } v ? $"?version={v}" : "");
        var env = await GetOrNullAsync<KvReadEnvelope>(url);
        return env?.Data;
    }

    public Task KvWriteAsync(string path, Dictionary<string, string> data, int? cas = null) =>
        SendAsync(HttpMethod.Post, $"v1/secret/data/{path}",
            cas is { } c ? new { data, options = new { cas = c } } : new { data });

    public Task KvDeleteAsync(string path) => SendAsync(HttpMethod.Delete, $"v1/secret/data/{path}");

    // ----- policies -----

    public async Task<IReadOnlyList<string>> PoliciesAsync()
    {
        var env = await GetAsync<KeysEnvelope>("v1/sys/policies");
        return env.Data?.Keys ?? new List<string>();
    }

    public async Task<PolicyDto?> PolicyAsync(string name)
    {
        var env = await GetOrNullAsync<PolicyEnvelope>($"v1/sys/policy/{name}");
        return env?.Data;
    }

    public Task PutPolicyAsync(string name, List<PolicyRuleDto> rules) =>
        SendAsync(HttpMethod.Post, $"v1/sys/policy/{name}",
            new { rules = rules.Select(r => new { path = r.Path, capabilities = r.Capabilities }) });

    public Task<ExplainDto> ExplainAsync(string path, string capability, IEnumerable<string> policies) =>
        PostAsync<ExplainDto>("v1/sys/policy/explain", new { path, capability, policies });

    // ----- audit -----

    public Task<AuditVerifyDto> AuditVerifyAsync() => PostAsync<AuditVerifyDto>("v1/sys/audit/verify", new { });

    // ----- leases -----

    public async Task<IReadOnlyList<string>> LeasesAsync()
    {
        var env = await GetAsync<KeysEnvelope>("v1/sys/leases");
        return env.Data?.Keys ?? new List<string>();
    }

    public Task RevokeLeaseAsync(string leaseId) =>
        SendAsync(HttpMethod.Post, "v1/sys/leases/revoke", new { lease_id = leaseId });

    // ----- AppRole -----

    public async Task<string> CreateAppRoleAsync(string name, IEnumerable<string> policies, int tokenTtl)
    {
        var env = await PostAsync<RoleIdEnvelope>($"v1/auth/approle/role/{name}",
            new { token_policies = policies, token_ttl = tokenTtl });
        return env.Data?.RoleId ?? "";
    }

    public async Task<string?> GetRoleIdAsync(string name)
    {
        var env = await GetOrNullAsync<RoleIdEnvelope>($"v1/auth/approle/role/{name}/role-id");
        return env?.Data?.RoleId;
    }

    public Task<SecretIdEnvelope> GenerateSecretIdAsync(string name, int? wrapTtl = null) =>
        PostAsync<SecretIdEnvelope>($"v1/auth/approle/role/{name}/secret-id",
            wrapTtl is { } w ? new { wrap_ttl = w } : new { });

    public async Task<string?> AppRoleLoginAsync(string roleId, string secretId)
    {
        var env = await PostAsync<AuthEnvelope>("v1/auth/approle/login",
            new { role_id = roleId, secret_id = secretId });
        return env.Auth?.ClientToken;
    }

    // ----- transit -----

    public Task CreateTransitKeyAsync(string name) =>
        SendAsync(HttpMethod.Post, $"v1/transit/keys/{name}");

    public async Task<string> EncryptAsync(string name, string base64Plaintext)
    {
        var env = await PostAsync<DataEnvelope>($"v1/transit/encrypt/{name}", new { plaintext = base64Plaintext });
        return env.Data?.GetValueOrDefault("ciphertext") ?? "";
    }

    public async Task<string> DecryptAsync(string name, string ciphertext)
    {
        var env = await PostAsync<DataEnvelope>($"v1/transit/decrypt/{name}", new { ciphertext });
        return env.Data?.GetValueOrDefault("plaintext") ?? "";
    }

    // ----- mounts -----

    public async Task<IReadOnlyList<MountDto>> MountsAsync()
    {
        var env = await GetAsync<MountsEnvelope>("v1/sys/mounts");
        return env.Data?.Mounts ?? new List<MountDto>();
    }

    // ============================ plumbing ============================

    private async Task<T> GetAsync<T>(string url)
    {
        using var resp = await Send(HttpMethod.Get, url);
        await EnsureOk(resp);
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<T?> GetOrNullAsync<T>(string url) where T : class
    {
        using var resp = await Send(HttpMethod.Get, url);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureOk(resp);
        return await resp.Content.ReadFromJsonAsync<T>();
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        using var resp = await Send(HttpMethod.Post, url, body);
        await EnsureOk(resp);
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var resp = await Send(method, url, body);
        await EnsureOk(resp);
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.TryAddWithoutValidation("X-Vault-Token", _auth.Token);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return _http.SendAsync(req);
    }

    private static async Task EnsureOk(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;

        var message = $"HTTP {(int)resp.StatusCode}";
        try
        {
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0)
                message = string.Join("; ", errs.EnumerateArray().Select(e => e.GetString()));
        }
        catch { /* keep the status-code message */ }

        throw new ApiException((int)resp.StatusCode, message);
    }
}
