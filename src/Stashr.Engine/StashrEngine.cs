using Stashr.Core.Audit;
using Stashr.Core.Authorization;
using Stashr.Core.Cryptography;
using Stashr.Core.Storage;
using Stashr.Engine.Audit;
using Stashr.Engine.Auth;
using Stashr.Engine.Authorization;
using Stashr.Engine.Engines;
using Stashr.Engine.Identity;
using Stashr.Engine.Kv;
using Stashr.Engine.Leases;
using Stashr.Engine.Mounts;
using Stashr.Engine.Seal;
using Stashr.Engine.Tokens;
using Stashr.Engine.Wrapping;

namespace Stashr.Engine;

/// <summary>
/// Composition root for the engine core: wires the seal manager, KV engine, policy evaluator,
/// audit chain and token manager over a single <see cref="ISecretStore"/> and crypto provider.
/// Hosts (the HTTP server, tests) construct one of these as a singleton.
/// </summary>
public sealed class StashrEngine : IDisposable
{
    public ISecretStore Store { get; }
    public KeyManager Keys { get; }
    public KvSecretsEngine Kv { get; }
    public IPolicyEvaluator Policy { get; }
    public AuditChain Audit { get; }
    public TokenManager Tokens { get; }
    public Router Router { get; }
    public WrappingService Wrapping { get; }
    public AppRoleAuth AppRole { get; }
    public LeaseManager Leases { get; }
    public IdentityStore Identity { get; }

    public StashrEngine(
        ISecretStore store,
        ICryptoProvider crypto,
        IAutoUnsealProvider? autoUnseal = null,
        IReadOnlyList<IAuditSink>? auditSinks = null)
    {
        Store = store;
        Keys = new KeyManager(store, crypto, autoUnseal);
        Kv = new KvSecretsEngine(store, Keys);
        Policy = new PolicyEvaluator();
        Audit = new AuditChain(store, crypto, Keys, auditSinks);
        Tokens = new TokenManager(store, Keys, crypto);
        Wrapping = new WrappingService(store, Keys, Tokens);
        Identity = new IdentityStore(store);
        AppRole = new AppRoleAuth(store, Keys, Tokens, crypto, Identity);
        Leases = new LeaseManager(store);

        Router = new Router(store, Keys);
        Router.MountEngine("cubbyhole/", new CubbyholeEngine());
        Router.MountEngine("transit/", new TransitEngine(Keys, crypto));
    }

    /// <summary>Apply storage schema, load seal state and the audit chain head. Engine starts sealed.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await Store.InitializeAsync(ct);
        await Keys.LoadStateAsync(ct);
        await Audit.LoadAsync(ct);
    }

    public void Dispose() => Keys.Dispose();
}
