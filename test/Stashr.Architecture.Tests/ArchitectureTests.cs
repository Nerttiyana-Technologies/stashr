using System.Reflection;
using NetArchTest.Rules;
using Stashr.Core.Storage;
using Stashr.Engine;
using Stashr.Storage.Postgres;
using Xunit;

namespace Stashr.Architecture.Tests;

/// <summary>
/// Build-gate architecture rules (ADR-0014). These guarantee the layering can't silently rot:
/// Stashr.Core stays infrastructure-free, the engine depends on storage *abstractions* not
/// concrete providers, and providers don't reach back into the engine.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(ISecretStore).Assembly;
    private static readonly Assembly Engine = typeof(StashrEngine).Assembly;
    private static readonly Assembly Postgres = typeof(PostgresSecretStore).Assembly;

    [Fact]
    public void Core_has_no_infrastructure_or_implementation_dependencies()
    {
        var result = Types.InAssembly(Core)
            .Should()
            .NotHaveDependencyOnAny(
                "Npgsql",
                "Microsoft.Data.SqlClient",
                "System.Net.Http",
                "Stashr.Crypto",
                "Stashr.Engine",
                "Stashr.Storage.Postgres",
                "Stashr.Storage.InMemory")
            .GetResult();

        Assert.True(result.IsSuccessful, Offenders(result));
    }

    [Fact]
    public void Engine_does_not_depend_on_concrete_storage_providers()
    {
        var result = Types.InAssembly(Engine)
            .Should()
            .NotHaveDependencyOnAny("Npgsql", "Stashr.Storage.Postgres", "Stashr.Storage.InMemory")
            .GetResult();

        Assert.True(result.IsSuccessful, Offenders(result));
    }

    [Fact]
    public void Storage_providers_do_not_depend_on_the_engine()
    {
        var result = Types.InAssembly(Postgres)
            .Should()
            .NotHaveDependencyOnAny("Stashr.Engine")
            .GetResult();

        Assert.True(result.IsSuccessful, Offenders(result));
    }

    private static string Offenders(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? new List<string>());
}
