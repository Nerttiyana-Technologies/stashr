using Stashr.Core.Engines;
using Stashr.Core.Storage;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Mounts;

/// <summary>
/// Routes requests to mounted engines by longest-prefix match (ADR-0009). Each engine is handed
/// a <see cref="StorageView"/> isolated to its mount and encrypted at rest under the DEK, so
/// engines can't reach each other's data and never persist plaintext.
/// </summary>
public sealed class Router(ISecretStore store, KeyManager keys)
{
    private readonly List<(string Path, ISecretsEngine Engine)> _mounts = new();

    /// <summary>Mount an engine at a path (e.g. "secret/", "cubbyhole/", "transit/").</summary>
    public void MountEngine(string path, ISecretsEngine engine)
    {
        var p = path.EndsWith('/') ? path : path + "/";
        _mounts.Add((p, engine));
        _mounts.Sort((a, b) => b.Path.Length.CompareTo(a.Path.Length)); // longest prefix first
    }

    public IReadOnlyList<string> Mounts => _mounts.Select(m => m.Path).ToList();

    public ISecretsEngine? EngineFor(string fullPath)
    {
        foreach (var (path, engine) in _mounts)
            if (fullPath.StartsWith(path, StringComparison.Ordinal))
                return engine;
        return null;
    }

    public async Task<EngineResponse> RouteAsync(
        EngineOp op, string fullPath, IReadOnlyDictionary<string, string>? data = null,
        string? tokenAccessor = null, CancellationToken ct = default)
    {
        foreach (var (path, engine) in _mounts)
        {
            if (!fullPath.StartsWith(path, StringComparison.Ordinal)) continue;
            var relative = fullPath.Substring(path.Length);
            var view = new StorageView(store, keys, "mounts/" + path);
            return await engine.HandleAsync(
                new EngineRequest { Operation = op, Path = relative, Data = data, TokenAccessor = tokenAccessor },
                view, ct).ConfigureAwait(false);
        }
        return EngineResponse.Missing;
    }
}
