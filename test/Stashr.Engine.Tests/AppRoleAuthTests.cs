using Stashr.Crypto;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class AppRoleAuthTests
{
    private static async Task<StashrEngine> UnsealedAsync()
    {
        var engine = new StashrEngine(new InMemorySecretStore(), new OsCryptoProvider());
        await engine.Keys.InitializeAsync(5, 3);
        return engine;
    }

    [Fact]
    public async Task Login_with_role_id_and_secret_id_issues_a_token_with_policies()
    {
        var engine = await UnsealedAsync();

        var roleId = await engine.AppRole.CreateRoleAsync("app", new[] { "app-read" },
            TimeSpan.FromHours(1), TimeSpan.Zero, secretIdNumUses: 0);
        var secretId = await engine.AppRole.GenerateSecretIdAsync("app");

        var login = await engine.AppRole.LoginAsync(roleId, secretId!);
        Assert.NotNull(login);

        // The issued token is valid and carries the role's policies.
        var info = await engine.Tokens.ValidateAsync(login!.Value.Token);
        Assert.NotNull(info);
        Assert.Contains("app-read", info!.Policies);
    }

    [Fact]
    public async Task Login_with_wrong_secret_id_fails()
    {
        var engine = await UnsealedAsync();
        var roleId = await engine.AppRole.CreateRoleAsync("app", new[] { "p" },
            TimeSpan.FromHours(1), TimeSpan.Zero, 0);
        await engine.AppRole.GenerateSecretIdAsync("app");

        Assert.Null(await engine.AppRole.LoginAsync(roleId, "deadbeef"));
    }

    [Fact]
    public async Task Login_with_unknown_role_id_fails()
    {
        var engine = await UnsealedAsync();
        Assert.Null(await engine.AppRole.LoginAsync("no-such-role", "no-such-secret"));
    }

    [Fact]
    public async Task Single_use_secret_id_works_once()
    {
        var engine = await UnsealedAsync();
        var roleId = await engine.AppRole.CreateRoleAsync("once", new[] { "p" },
            TimeSpan.FromHours(1), TimeSpan.Zero, secretIdNumUses: 1);
        var secretId = await engine.AppRole.GenerateSecretIdAsync("once");

        Assert.NotNull(await engine.AppRole.LoginAsync(roleId, secretId!));
        Assert.Null(await engine.AppRole.LoginAsync(roleId, secretId!)); // exhausted
    }

    [Fact]
    public async Task Role_id_is_stable_across_role_updates()
    {
        var engine = await UnsealedAsync();
        var id1 = await engine.AppRole.CreateRoleAsync("r", new[] { "a" }, TimeSpan.FromHours(1), TimeSpan.Zero, 0);
        var id2 = await engine.AppRole.CreateRoleAsync("r", new[] { "a", "b" }, TimeSpan.FromHours(1), TimeSpan.Zero, 0);
        Assert.Equal(id1, id2);
    }
}
