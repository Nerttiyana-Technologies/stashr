using System.Text.Json;
using Stashr.Core.Engines;
using Stashr.Core.Storage;

namespace Stashr.Engine.Engines;

/// <summary>
/// The cubbyhole engine (ADR-0011): a private per-token store. Data is namespaced by the
/// calling token's accessor, so one token can never read another's cubbyhole. When a token is
/// revoked, the token manager hard-deletes its cubbyhole (cascading sweep).
/// </summary>
public sealed class CubbyholeEngine : ISecretsEngine
{
    public string Type => "cubbyhole";

    public async Task<EngineResponse> HandleAsync(EngineRequest request, IStorageView storage, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(request.TokenAccessor))
            return EngineResponse.Missing; // cubbyhole requires a token

        var key = $"{request.TokenAccessor}/{request.Path}";
        switch (request.Operation)
        {
            case EngineOp.Write:
                await storage.PutAsync(key,
                    JsonSerializer.SerializeToUtf8Bytes(request.Data ?? new Dictionary<string, string>()), ct);
                return EngineResponse.Empty;

            case EngineOp.Read:
                var bytes = await storage.GetAsync(key, ct);
                return bytes is null
                    ? EngineResponse.Missing
                    : new EngineResponse { Data = JsonSerializer.Deserialize<Dictionary<string, string>>(bytes) };

            case EngineOp.List:
                var prefix = $"{request.TokenAccessor}/{request.Path}";
                var keys = await storage.ListAsync(prefix, ct);
                return new EngineResponse { Keys = keys.Select(k => k.Substring(prefix.Length).TrimStart('/')).ToList() };

            case EngineOp.Delete:
                await storage.DeleteAsync(key, ct);
                return EngineResponse.Empty;

            default:
                return EngineResponse.Missing;
        }
    }
}
