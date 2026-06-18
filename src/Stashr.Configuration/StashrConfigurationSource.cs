using Microsoft.Extensions.Configuration;
using Stashr.Client;

namespace Stashr.Configuration;

/// <summary>The <see cref="IConfigurationSource"/> for stashr (ADR-0013).</summary>
public sealed class StashrConfigurationSource : IConfigurationSource
{
    private readonly StashrConfigurationOptions _options;

    public StashrConfigurationSource(StashrConfigurationOptions options) => _options = options;

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new StashrConfigurationProvider(_options);
}

/// <summary>
/// Loads stashr secrets into the configuration tree. Each configured secret's key/value pairs
/// become configuration entries (store a key like <c>ConnectionStrings:Db</c> and existing
/// <c>_config["ConnectionStrings:Db"]</c> calls keep working). Fail-fast unless Optional.
/// </summary>
public sealed class StashrConfigurationProvider : ConfigurationProvider
{
    private readonly StashrConfigurationOptions _options;

    public StashrConfigurationProvider(StashrConfigurationOptions options) => _options = options;

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var client = new StashrClient(new StashrClientOptions
            {
                Address = _options.Address,
                Token = _options.Token,
                Mount = _options.Mount,
            });

            foreach (var path in _options.Paths)
            {
                // Configuration load is synchronous; block on the async client at startup.
                var secret = client.ReadKvAsync(path).GetAwaiter().GetResult();
                if (secret is null)
                {
                    if (!_options.Optional)
                        throw new InvalidOperationException($"stashr: secret path '{path}' was not found.");
                    continue;
                }
                foreach (var kv in secret) data[kv.Key] = kv.Value;
            }
        }
        catch when (_options.Optional)
        {
            // Optional source: leave whatever loaded; never fall back to plaintext on disk.
        }

        Data = data;
    }
}
