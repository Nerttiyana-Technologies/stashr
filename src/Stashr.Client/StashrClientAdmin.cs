using System.Linq;
using System.Net;
using System.Text.Json;

namespace Stashr.Client;

/// <summary>Operator/admin operations (sys, policy, audit) used by the CLI and tooling.</summary>
public sealed partial class StashrClient
{
    public async Task<SealStatusInfo> GetStatusAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "/v1/sys/seal-status", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        return ParseStatus(doc.RootElement);
    }

    public async Task<InitInfo> InitAsync(int shares, int threshold, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { secret_shares = shares, secret_threshold = threshold });
        using var resp = await SendAsync(HttpMethod.Post, "/v1/sys/init", body, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var r = doc.RootElement;

        var keys = new List<string>();
        if (r.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.Array)
            foreach (var e in k.EnumerateArray()) keys.Add(e.GetString() ?? string.Empty);

        return new InitInfo
        {
            Keys = keys,
            RootToken = r.TryGetProperty("root_token", out var t) ? t.GetString() ?? string.Empty : string.Empty,
        };
    }

    public async Task<SealStatusInfo> UnsealAsync(string key, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { key });
        using var resp = await SendAsync(HttpMethod.Post, "/v1/sys/unseal", body, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        return ParseStatus(doc.RootElement);
    }

    public async Task SealAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "/v1/sys/seal", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListKvAsync(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"/v1/{_options.Mount}/metadata/{path}?list=true", null, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return Array.Empty<string>();
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var keys = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var d) &&
            d.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.Array)
            foreach (var e in k.EnumerateArray()) keys.Add(e.GetString() ?? string.Empty);
        return keys;
    }

    public async Task DeleteKvAsync(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"/v1/{_options.Mount}/data/{path}", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
    }

    public async Task<AuditVerifyInfo> VerifyAuditAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "/v1/sys/audit/verify", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var r = doc.RootElement;
        long? broken = r.TryGetProperty("first_broken_seq", out var fb) && fb.ValueKind == JsonValueKind.Number
            ? fb.GetInt64()
            : null;
        return new AuditVerifyInfo
        {
            Valid = r.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True,
            FirstBrokenSeq = broken,
            Checked = r.TryGetProperty("checked_count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0,
        };
    }

    public async Task<ExplainInfo> ExplainAsync(string path, string capability, IEnumerable<string> policies, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { path, capability, policies = policies.ToArray() });
        using var resp = await SendAsync(HttpMethod.Post, "/v1/sys/policy/explain", body, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var r = doc.RootElement;
        return new ExplainInfo
        {
            Allowed = r.TryGetProperty("allowed", out var a) && a.ValueKind == JsonValueKind.True,
            Explanation = r.TryGetProperty("explanation", out var e) ? e.GetString() ?? string.Empty : string.Empty,
        };
    }

    private static SealStatusInfo ParseStatus(JsonElement r) => new()
    {
        Initialized = r.TryGetProperty("initialized", out var i) && i.ValueKind == JsonValueKind.True,
        Sealed = r.TryGetProperty("sealed", out var s) && s.ValueKind == JsonValueKind.True,
        Threshold = r.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0,
        TotalShares = r.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0,
        Progress = r.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0,
    };
}
