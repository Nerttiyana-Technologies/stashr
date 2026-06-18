using System.Net;
using System.Text;
using System.Text.Json;

namespace Stashr.Client;

/// <summary>Configuration for a <see cref="StashrClient"/> (ADR-0013).</summary>
public sealed class StashrClientOptions
{
    /// <summary>Base address of the stashr server, e.g. https://stashr.internal:8200.</summary>
    public string Address { get; set; } = "http://localhost:8200";

    /// <summary>Auth token sent as the X-Vault-Token header.</summary>
    public string? Token { get; set; }

    /// <summary>KV mount name (default "secret").</summary>
    public string Mount { get; set; } = "secret";
}

/// <summary>An error returned by the stashr server.</summary>
public sealed class StashrException : Exception
{
    public int StatusCode { get; }
    public StashrException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

/// <summary>
/// A thin, idiomatic .NET client over the stashr (Vault-compatible) HTTP API (ADR-0003/0013).
/// No runtime reflection; just HttpClient + System.Text.Json. Reusable from modern .NET and,
/// via the netstandard2.0 target, from .NET Framework.
/// </summary>
public sealed partial class StashrClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly StashrClientOptions _options;
    private readonly bool _ownsHttp;

    public StashrClient(StashrClientOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
        _http.BaseAddress = new Uri(_options.Address);
    }

    /// <summary>Read a KV v2 secret's data map. Returns null if the path does not exist.</summary>
    public async Task<IReadOnlyDictionary<string, string>?> ReadKvAsync(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"/v1/{_options.Mount}/data/{path}", null, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp).ConfigureAwait(false);

        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var result = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("data", out var outer) &&
            outer.TryGetProperty("data", out var inner) &&
            inner.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in inner.EnumerateObject())
                result[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }
        return result;
    }

    /// <summary>Write a KV v2 secret; returns the new version number.</summary>
    public async Task<int> WriteKvAsync(string path, IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { data });
        using var resp = await SendAsync(HttpMethod.Post, $"/v1/{_options.Mount}/data/{path}", payload, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);

        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("version", out var v)
            ? v.GetInt32()
            : 0;
    }

    /// <summary>Whether the server is currently sealed.</summary>
    public async Task<bool> IsSealedAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "/v1/sys/seal-status", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("sealed", out var s) && s.GetBoolean();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? jsonBody, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrEmpty(_options.Token))
            req.Headers.TryAddWithoutValidation("X-Vault-Token", _options.Token);
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await _http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new StashrException((int)resp.StatusCode, $"stashr request failed ({(int)resp.StatusCode}): {body}");
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
