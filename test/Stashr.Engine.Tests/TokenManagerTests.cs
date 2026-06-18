using Stashr.Crypto;
using Stashr.Engine.Seal;
using Stashr.Engine.Tokens;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class TokenManagerTests
{
    private static async Task<TokenManager> NewAsync()
    {
        var store = new InMemorySecretStore();
        var crypto = new OsCryptoProvider();
        var km = new KeyManager(store, crypto);
        await km.InitializeAsync(5, 3);
        return new TokenManager(store, km, crypto);
    }

    [Fact]
    public async Task Created_token_validates_and_carries_policies()
    {
        var tm = await NewAsync();
        var (token, info) = await tm.CreateServiceTokenAsync(new[] { "app-read" }, TimeSpan.FromHours(1));

        var validated = await tm.ValidateAsync(token);
        Assert.NotNull(validated);
        Assert.Equal(info.Accessor, validated!.Accessor);
        Assert.Contains("app-read", validated.Policies);
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        var tm = await NewAsync();
        Assert.Null(await tm.ValidateAsync("not-a-token"));
        Assert.Null(await tm.ValidateAsync("s.deadbeef.cafe"));
    }

    [Fact]
    public async Task Tampered_secret_is_rejected()
    {
        var tm = await NewAsync();
        var (token, _) = await tm.CreateServiceTokenAsync(new[] { "p" }, TimeSpan.FromHours(1));

        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{new string('0', parts[2].Length)}";

        Assert.Null(await tm.ValidateAsync(tampered));
    }

    [Fact]
    public async Task Revoked_token_no_longer_validates()
    {
        var tm = await NewAsync();
        var (token, info) = await tm.CreateServiceTokenAsync(new[] { "p" }, TimeSpan.FromHours(1));

        await tm.RevokeAsync(info.Accessor);
        Assert.Null(await tm.ValidateAsync(token));
    }
}
