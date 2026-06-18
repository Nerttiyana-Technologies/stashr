using Stashr.Core.Model;
using Stashr.Engine;
using Stashr.Engine.Seal;

namespace Stashr.Server;

/// <summary>Bootstrap + request-authentication helpers for the host.</summary>
internal static class Bootstrap
{
    /// <summary>
    /// Initialize the engine (Shamir split), create the root policy + a root token, and return
    /// the token plus the share set. The engine is left unsealed (init holds the keys).
    /// </summary>
    public static async Task<(string RootToken, InitResult Init)> InitializeAsync(
        StashrEngine engine, int shares, int threshold, CancellationToken ct = default)
    {
        var init = await engine.Keys.InitializeAsync(shares, threshold, ct);

        await engine.Store.PutPolicyAsync(new Policy
        {
            Name = "root",
            Rules = new[]
            {
                new PolicyRule
                {
                    PathPattern = "*",
                    Capabilities = Capability.Create | Capability.Read | Capability.Update
                                 | Capability.Delete | Capability.List | Capability.Sudo,
                },
            },
        }, ct);

        var (token, _) = await engine.Tokens.CreateServiceTokenAsync(
            new[] { "root" }, TimeSpan.FromDays(365), ct);
        return (token, init);
    }

    /// <summary>Validate the token header (Vault-compatible). Returns null if missing/invalid.</summary>
    public static async Task<TokenInfo?> AuthenticateAsync(HttpContext http, StashrEngine engine)
    {
        var token = http.Request.Headers["X-Vault-Token"].FirstOrDefault()
                    ?? http.Request.Headers["X-Stashr-Token"].FirstOrDefault();
        return string.IsNullOrEmpty(token) ? null : await engine.Tokens.ValidateAsync(token);
    }
}
