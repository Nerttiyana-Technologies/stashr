using Microsoft.Extensions.Configuration;

namespace Stashr.Configuration;

/// <summary>
/// <c>builder.Configuration.AddStashr(...)</c> — registers stashr as a configuration source so
/// secrets resolve through the standard <see cref="IConfiguration"/> with no app-code rewrite
/// (ADR-0013).
/// </summary>
public static class StashrConfigurationExtensions
{
    public static IConfigurationBuilder AddStashr(
        this IConfigurationBuilder builder, Action<StashrConfigurationOptions> configure)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var options = new StashrConfigurationOptions();
        configure(options);
        return builder.Add(new StashrConfigurationSource(options));
    }
}
