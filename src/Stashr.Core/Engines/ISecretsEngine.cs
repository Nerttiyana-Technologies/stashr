using Stashr.Core.Storage;

namespace Stashr.Core.Engines;

/// <summary>The logical operation a request performs on an engine (ADR-0009).</summary>
public enum EngineOp
{
    Read,
    Write,
    List,
    Delete,
}

/// <summary>A request routed to a mounted engine. <see cref="Path"/> is relative to the mount.</summary>
public sealed record EngineRequest
{
    public required EngineOp Operation { get; init; }
    public required string Path { get; init; }
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>Accessor of the calling token; used by per-token engines like cubbyhole.</summary>
    public string? TokenAccessor { get; init; }
}

/// <summary>The result of handling an <see cref="EngineRequest"/>.</summary>
public sealed record EngineResponse
{
    public IReadOnlyDictionary<string, string>? Data { get; init; }
    public IReadOnlyList<string>? Keys { get; init; }
    public bool NotFound { get; init; }

    public static EngineResponse Empty { get; } = new();
    public static EngineResponse Missing { get; } = new() { NotFound = true };
}

/// <summary>
/// A secrets engine mounted at a path (KV, cubbyhole, transit, …) — ADR-0009. It receives
/// requests scoped to its mount and a prefix-isolated <see cref="IStorageView"/>; it never sees
/// other mounts' data, the master key, or the raw store.
/// </summary>
public interface ISecretsEngine
{
    /// <summary>The engine type identifier (e.g. "kv", "cubbyhole", "transit").</summary>
    string Type { get; }

    Task<EngineResponse> HandleAsync(EngineRequest request, IStorageView storage, CancellationToken ct = default);
}
