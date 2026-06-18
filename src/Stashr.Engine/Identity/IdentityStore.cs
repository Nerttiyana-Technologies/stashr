using System.Text;
using System.Text.Json;
using Stashr.Core.Storage;

namespace Stashr.Engine.Identity;

/// <summary>A durable identity that auth aliases map onto (ADR-0008/0009).</summary>
public sealed record Entity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>A named group of entities; its policies apply to every member.</summary>
public sealed record Group
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MemberEntityIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Identity store (ADR-0008/0009): entities, groups, and aliases that bind an auth principal
/// (e.g. <c>approle/app</c>) to an entity. At login, a token's policies become the union of its
/// base policies, its entity's policies, and the policies of every group the entity belongs to.
/// Identity records are configuration (policy names, not secrets) so they live as plain blobs.
/// </summary>
public sealed class IdentityStore(ISecretStore store)
{
    public async Task<string> CreateEntityAsync(
        string name, IReadOnlyList<string> policies, IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var id = await ResolveNameAsync(name, ct) ?? Guid.NewGuid().ToString("n");
        var entity = new Entity
        {
            Id = id,
            Name = name,
            Policies = policies,
            Metadata = metadata ?? new Dictionary<string, string>(),
        };
        await store.PutBlobAsync($"identity/entity/{id}", JsonSerializer.SerializeToUtf8Bytes(entity), ct);
        await store.PutBlobAsync($"identity/entity-name/{name}", Encoding.UTF8.GetBytes(id), ct);
        return id;
    }

    public async Task<Entity?> GetEntityAsync(string id, CancellationToken ct = default)
    {
        var b = await store.GetBlobAsync($"identity/entity/{id}", ct);
        return b is null ? null : JsonSerializer.Deserialize<Entity>(b);
    }

    public async Task<string?> ResolveNameAsync(string name, CancellationToken ct = default)
    {
        var b = await store.GetBlobAsync($"identity/entity-name/{name}", ct);
        return b is null ? null : Encoding.UTF8.GetString(b);
    }

    public Task CreateGroupAsync(
        string name, IReadOnlyList<string> policies, IReadOnlyList<string> memberEntityIds, CancellationToken ct = default)
    {
        var group = new Group { Name = name, Policies = policies, MemberEntityIds = memberEntityIds };
        return store.PutBlobAsync($"identity/group/{name}", JsonSerializer.SerializeToUtf8Bytes(group), ct);
    }

    public Task CreateAliasAsync(string principal, string entityId, CancellationToken ct = default)
        => store.PutBlobAsync($"identity/alias/{principal}", Encoding.UTF8.GetBytes(entityId), ct);

    public async Task<string?> ResolveAliasAsync(string principal, CancellationToken ct = default)
    {
        var b = await store.GetBlobAsync($"identity/alias/{principal}", ct);
        return b is null ? null : Encoding.UTF8.GetString(b);
    }

    /// <summary>Union of base policies, the entity's policies, and every containing group's policies.</summary>
    public async Task<IReadOnlyList<string>> ResolveEffectivePoliciesAsync(
        string entityId, IReadOnlyList<string> basePolicies, CancellationToken ct = default)
    {
        var set = new HashSet<string>(basePolicies, StringComparer.Ordinal);

        var entity = await GetEntityAsync(entityId, ct);
        if (entity is not null)
        {
            foreach (var p in entity.Policies) set.Add(p);

            foreach (var key in await store.ListBlobAsync("identity/group/", ct))
            {
                var gb = await store.GetBlobAsync(key, ct);
                if (gb is null) continue;
                var group = JsonSerializer.Deserialize<Group>(gb)!;
                if (group.MemberEntityIds.Contains(entityId))
                    foreach (var p in group.Policies) set.Add(p);
            }
        }

        return set.ToList();
    }
}
