using Stashr.Crypto;
using Stashr.Engine.Identity;
using Stashr.Storage.InMemory;
using Xunit;

namespace Stashr.Engine.Tests;

public class IdentityTests
{
    [Fact]
    public async Task Entity_round_trips_by_id_and_name()
    {
        var id = new IdentityStore(new InMemorySecretStore());
        var entityId = await id.CreateEntityAsync("alice", new[] { "app-read" });

        Assert.Equal(entityId, await id.ResolveNameAsync("alice"));
        var e = await id.GetEntityAsync(entityId);
        Assert.Equal("alice", e!.Name);
        Assert.Contains("app-read", e.Policies);
    }

    [Fact]
    public async Task Effective_policies_union_entity_and_group_policies()
    {
        var id = new IdentityStore(new InMemorySecretStore());
        var entityId = await id.CreateEntityAsync("svc", new[] { "entity-pol" });
        await id.CreateGroupAsync("platform", new[] { "group-pol" }, new[] { entityId });

        var effective = await id.ResolveEffectivePoliciesAsync(entityId, new[] { "base-pol" });

        Assert.Contains("base-pol", effective);    // base
        Assert.Contains("entity-pol", effective);  // entity
        Assert.Contains("group-pol", effective);   // group membership
    }

    [Fact]
    public async Task Non_member_does_not_inherit_group_policies()
    {
        var id = new IdentityStore(new InMemorySecretStore());
        var member = await id.CreateEntityAsync("m", Array.Empty<string>());
        var outsider = await id.CreateEntityAsync("o", Array.Empty<string>());
        await id.CreateGroupAsync("g", new[] { "secret-pol" }, new[] { member });

        var effective = await id.ResolveEffectivePoliciesAsync(outsider, Array.Empty<string>());
        Assert.DoesNotContain("secret-pol", effective);
    }

    [Fact]
    public async Task AppRole_login_inherits_entity_and_group_policies_via_alias()
    {
        var engine = new StashrEngine(new InMemorySecretStore(), new OsCryptoProvider());
        await engine.Keys.InitializeAsync(5, 3);

        // identity: entity in a group, aliased to the approle role
        var entityId = await engine.Identity.CreateEntityAsync("payments-svc", new[] { "entity-pol" });
        await engine.Identity.CreateGroupAsync("platform", new[] { "group-pol" }, new[] { entityId });
        await engine.Identity.CreateAliasAsync("approle/payments", entityId);

        var roleId = await engine.AppRole.CreateRoleAsync("payments", new[] { "role-pol" },
            TimeSpan.FromHours(1), TimeSpan.Zero, 0);
        var secretId = await engine.AppRole.GenerateSecretIdAsync("payments");

        var login = await engine.AppRole.LoginAsync(roleId, secretId!);
        var info = await engine.Tokens.ValidateAsync(login!.Value.Token);

        Assert.NotNull(info);
        Assert.Contains("role-pol", info!.Policies);    // from the role
        Assert.Contains("entity-pol", info.Policies);   // from the aliased entity
        Assert.Contains("group-pol", info.Policies);    // from the entity's group
    }
}
