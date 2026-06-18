using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Configuration.ConfigurationBuilders;

namespace Stashr.Configuration.Legacy
{
    /// <summary>
    /// A classic .NET Framework <c>ConfigurationBuilder</c> that resolves <c>appSettings</c> and
    /// <c>connectionStrings</c> values from a stashr KV v2 secret — so legacy app.config / web.config
    /// apps stop shipping plaintext secrets (ADR-0013).
    ///
    /// Register it in web.config:
    /// <code>
    /// &lt;configBuilders&gt;
    ///   &lt;builders&gt;
    ///     &lt;add name="Stashr"
    ///          mode="Greedy"
    ///          address="https://stashr.internal:8200"
    ///          mount="secret"
    ///          path="myapp/prod"
    ///          tokenEnv="STASHR_TOKEN"
    ///          type="Stashr.Configuration.Legacy.StashrConfigBuilder, Stashr.Configuration.Legacy" /&gt;
    ///   &lt;/builders&gt;
    /// &lt;/configBuilders&gt;
    /// &lt;appSettings configBuilders="Stashr"&gt; ... &lt;/appSettings&gt;
    /// </code>
    /// </summary>
    public sealed class StashrConfigBuilder : KeyValueConfigBuilder
    {
        private string _address;
        private string _mount;
        private string _path;
        private string _token;
        private Dictionary<string, string> _cache;

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            _address = config["address"]
                ?? Environment.GetEnvironmentVariable("STASHR_ADDR")
                ?? "https://127.0.0.1:8200";
            _mount = config["mount"] ?? "secret";
            _path = config["path"] ?? string.Empty;

            // Token: prefer an env var name (so it never sits in config) but allow an inline token.
            var tokenEnv = config["tokenEnv"];
            _token = (!string.IsNullOrEmpty(tokenEnv) ? Environment.GetEnvironmentVariable(tokenEnv) : null)
                ?? config["token"]
                ?? Environment.GetEnvironmentVariable("STASHR_TOKEN")
                ?? string.Empty;

            // .NET Framework defaults can predate TLS 1.2; stashr requires modern TLS.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>Resolve a single key (returns null when absent, per the base contract).</summary>
        public override string GetValue(string key)
        {
            var data = LoadSecret();
            return data != null && data.TryGetValue(key, out var v) ? v : null;
        }

        /// <summary>Resolve all keys under a prefix (used by Greedy mode).</summary>
        public override ICollection<KeyValuePair<string, string>> GetAllValues(string prefix)
        {
            var result = new List<KeyValuePair<string, string>>();
            var data = LoadSecret();
            if (data == null) return result;

            foreach (var kv in data)
                if (string.IsNullOrEmpty(prefix) ||
                    kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(kv);

            return result;
        }

        private Dictionary<string, string> LoadSecret()
        {
            if (_cache != null) return _cache;

            try
            {
                using (var http = new HttpClient())
                {
                    if (!string.IsNullOrEmpty(_token))
                        http.DefaultRequestHeaders.Add("X-Vault-Token", _token);

                    var url = $"{_address.TrimEnd('/')}/v1/{_mount}/data/{_path}";
                    var json = http.GetStringAsync(url).GetAwaiter().GetResult();

                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        // KV v2 shape: { "data": { "data": { k: v, ... }, "metadata": {...} } }
                        if (doc.RootElement.TryGetProperty("data", out var outer) &&
                            outer.TryGetProperty("data", out var inner) &&
                            inner.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in inner.EnumerateObject())
                                dict[p.Name] = p.Value.ValueKind == JsonValueKind.String
                                    ? p.Value.GetString()
                                    : p.Value.GetRawText();
                        }
                    }

                    _cache = dict;
                }
            }
            catch (Exception ex)
            {
                throw new ConfigurationErrorsException(
                    $"stashr config builder '{Name}' could not read secret '{_mount}/{_path}' from {_address}: {ex.Message}", ex);
            }

            return _cache;
        }
    }
}
