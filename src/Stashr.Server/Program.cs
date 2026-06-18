using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Stashr.Core.Cryptography;
using Stashr.Core.Engines;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Crypto;
using Stashr.Engine;
using Stashr.Engine.Ha;
using Stashr.Engine.Seal;
using Stashr.Engines.Database;
using Stashr.Server;
using Stashr.Storage.InMemory;
using Stashr.Storage.Postgres;

// Harden the process before anything sensitive happens: no core dumps to spill the master key.
var coreDumpsDisabled = Stashr.Crypto.ProcessHardening.TryDisableCoreDumps();

var builder = WebApplication.CreateBuilder(args);

// TLS (ADR-0002): when a certificate is configured, listen HTTPS — and optionally require client
// certificates (mTLS). With no cert configured the host stays HTTP (dev). Production must set this.
builder.WebHost.ConfigureKestrel((context, options) =>
{
    var certPath = context.Configuration["Stashr:Tls:CertPath"];
    if (string.IsNullOrWhiteSpace(certPath)) return;
    if (!File.Exists(certPath))
        throw new InvalidOperationException(
            $"Stashr:Tls:CertPath points to '{Path.GetFullPath(certPath)}', which does not exist. " +
            "Provide an absolute path to a PKCS#12 (.pfx) certificate.");

    var cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, context.Configuration["Stashr:Tls:CertPassword"]);
    var requireClientCert = context.Configuration.GetValue<bool>("Stashr:Tls:RequireClientCertificate");
    var port = context.Configuration.GetValue<int?>("Stashr:Tls:Port") ?? 8200;

    options.ListenAnyIP(port, listen => listen.UseHttps(https =>
    {
        https.ServerCertificate = cert;
        if (requireClientCert)
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    }));
});

builder.Services.AddSingleton<ICryptoProvider, OsCryptoProvider>();
builder.Services.AddSingleton<ISecretStore>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var backend = cfg["Stashr:Storage"] ?? "InMemory";
    if (string.Equals(backend, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        var cs = cfg["Stashr:Postgres:ConnectionString"]
                 ?? throw new InvalidOperationException("Stashr:Postgres:ConnectionString is required when Storage=Postgres");
        return new PostgresSecretStore(cs);
    }
    return new InMemorySecretStore();
});
builder.Services.AddSingleton(sp =>
{
    var c = sp.GetRequiredService<ICryptoProvider>();
    var s = sp.GetRequiredService<ISecretStore>();
    var keyB64 = sp.GetRequiredService<IConfiguration>()["Stashr:AutoUnseal:Key"];
    IAutoUnsealProvider? autoUnseal = !string.IsNullOrWhiteSpace(keyB64)
        ? new StaticKeyAutoUnseal(c, Convert.FromBase64String(keyB64))
        : null;
    return new StashrEngine(s, c, autoUnseal);
});

var app = builder.Build();

var engine = app.Services.GetRequiredService<StashrEngine>();
var crypto = app.Services.GetRequiredService<ICryptoProvider>();
await engine.StartAsync();

// Fail-closed: a regulated deployment must run on a FIPS-validated module (ADR-0004).
if (app.Configuration.GetValue<bool>("Stashr:RequireFips") && !crypto.FipsMode)
    throw new InvalidOperationException("Stashr:RequireFips is set but no FIPS-validated cryptographic module is active.");

app.Logger.LogInformation("stashr: crypto backend = {Backend}; core-dump hardening applied = {Hardened}.",
    crypto.BackendDescription, coreDumpsDisabled);

// Auto-unseal at startup if configured and already initialized (ADR-0012).
if (engine.Keys.IsInitialized && engine.Keys.IsSealed && await engine.Keys.TryAutoUnsealAsync())
    app.Logger.LogInformation("stashr: auto-unsealed at startup via the configured seal provider.");

// First-run init: dev mode, or when auto-unseal is configured (so a fresh prod store comes up usable).
var autoUnsealConfigured = !string.IsNullOrWhiteSpace(app.Configuration["Stashr:AutoUnseal:Key"]);
var haEnabled = app.Configuration.GetValue<bool>("Stashr:Ha:Enabled");
// In HA, an operator initializes the cluster once (so nodes don't race to init).
if (!engine.Keys.IsInitialized && !haEnabled && (app.Configuration.GetValue<bool>("Stashr:DevMode") || autoUnsealConfigured))
{
    var (token, init) = await Bootstrap.InitializeAsync(engine, 5, 3);
    var keyKind = autoUnsealConfigured ? "recovery keys" : "unseal keys";
    app.Logger.LogWarning(
        "stashr initialized ({Mode}). Root token: {Token}. {KeyKind} (store securely): {Keys}",
        autoUnsealConfigured ? "auto-unseal" : "dev",
        token, keyKind, string.Join(" | ", init.Shares));
}

// Mount the dynamic database secrets engine if configured (admin connection present).
var dbOptions = app.Configuration.GetSection("Stashr:Database").Get<DatabaseEngineOptions>();
if (dbOptions is not null && !string.IsNullOrWhiteSpace(dbOptions.AdminConnectionString))
{
    var dbEngine = new DatabaseSecretsEngine(engine.Leases, dbOptions);
    engine.Router.MountEngine("database/", dbEngine);
    engine.Leases.RegisterRevoker(dbEngine);
    app.Logger.LogInformation("stashr: database secrets engine mounted (database/) with {Count} role(s).", dbOptions.Roles.Count);
}

// Background lease-expiration sweep (ADR-0006): auto-revoke expired leases.
_ = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
    while (await timer.WaitForNextTickAsync())
    {
        try { await engine.Leases.RevokeExpiredAsync(); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "lease sweep failed"); }
    }
});

// High availability (ADR-0002): contend for leadership; auto-unseal on becoming active,
// fail-closed self-seal on losing it. Full request-forwarding/partition handling is deploy-side.
if (haEnabled)
{
    var pgConn = app.Configuration["Stashr:Postgres:ConnectionString"];
    if (string.IsNullOrWhiteSpace(pgConn))
    {
        app.Logger.LogWarning("Stashr:Ha:Enabled is set but Stashr:Postgres:ConnectionString is empty — HA disabled.");
    }
    else
    {
        var ha = new HaCoordinator(new PostgresLeaderElection(pgConn), engine.Keys);
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            var wasLeader = false;
            do
            {
                try
                {
                    await ha.TickAsync();
                    if (ha.IsLeader && engine.Keys.IsSealed)
                        await engine.Keys.TryAutoUnsealAsync(); // unseal once initialized

                    if (ha.IsLeader && !wasLeader)
                        app.Logger.LogInformation("stashr: became ACTIVE (fencing epoch {Epoch}).", ha.Epoch);
                    else if (!ha.IsLeader && wasLeader)
                        app.Logger.LogWarning("stashr: lost leadership → sealed, now STANDBY.");
                    wasLeader = ha.IsLeader;
                }
                catch (Exception ex) { app.Logger.LogWarning(ex, "HA tick failed"); }
            }
            while (await timer.WaitForNextTickAsync());
        });
    }
}

// --- Web UI (ADR-0017): serve the Blazor WebAssembly admin console under /ui ---
app.UseBlazorFrameworkFiles("/ui");
app.UseStaticFiles();

var status = new StatusMapper();

// --- sys: lifecycle (unauthenticated bootstrap) ---

app.MapGet("/v1/sys/health", () =>
    Results.Json(new { initialized = engine.Keys.IsInitialized, @sealed = engine.Keys.IsSealed, version = "0.1.0-dev" }));

app.MapGet("/v1/sys/seal-status", () => Results.Json(status.ToDto(engine.Keys.Status())));

app.MapPost("/v1/sys/init", async (InitRequest req) =>
{
    if (engine.Keys.IsInitialized) return Results.Conflict(new { errors = new[] { "already initialized" } });
    var (token, init) = await Bootstrap.InitializeAsync(engine, req.SecretShares ?? 5, req.SecretThreshold ?? 3);
    return Results.Json(new
    {
        keys = init.Shares,
        secret_shares = init.TotalShares,
        secret_threshold = init.Threshold,
        root_token = token,
    });
});

app.MapPost("/v1/sys/unseal", async (UnsealRequest req) =>
{
    if (string.IsNullOrEmpty(req.Key)) return Results.BadRequest(new { errors = new[] { "key is required" } });
    var s = await engine.Keys.SubmitUnsealShareAsync(req.Key);
    return Results.Json(status.ToDto(s));
});

app.MapPost("/v1/sys/seal", async (HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    engine.Keys.Seal();
    return Results.NoContent();
});

// --- KV v2 (token-authenticated AND policy-authorized) ---

app.MapGet("/v1/secret/data/{*path}", async (string? path, HttpContext http, int? version) =>
{
    path ??= string.Empty;
    var (_, err) = await Authorize(http, Capability.Read);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var v = await engine.Kv.ReadAsync(path, version);
    if (v is null) return Results.NotFound(new { errors = new[] { "not found" } });

    await SafeAudit(http, "read", path);
    return Results.Json(new
    {
        data = new
        {
            data = v.Data,
            metadata = new
            {
                version = v.Version,
                created_time = v.CreatedAt,
                deleted = v.DeletedAt is not null,
                destroyed = v.Destroyed,
            },
        },
    });
});

app.MapPost("/v1/secret/data/{*path}", async (string? path, KvWriteRequest req, HttpContext http) =>
{
    path ??= string.Empty;
    var (_, err) = await Authorize(http, Capability.Update);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var version = await engine.Kv.WriteAsync(path, req.Data ?? new Dictionary<string, string>(), req.Options?.Cas);
    await SafeAudit(http, "write", path);
    return Results.Json(new { data = new { version, created_time = DateTimeOffset.UtcNow } });
});

app.MapGet("/v1/secret/metadata/{*path}", async (string? path, HttpContext http, bool list = false) =>
{
    path ??= string.Empty;
    var (_, err) = await Authorize(http, list ? Capability.List : Capability.Read);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    if (list)
    {
        var keys = await engine.Kv.ListAsync(path);
        return Results.Json(new { data = new { keys } });
    }

    var meta = await engine.Kv.GetMetadataAsync(path);
    return meta is null
        ? Results.NotFound(new { errors = new[] { "not found" } })
        : Results.Json(new
        {
            data = new
            {
                current_version = meta.CurrentVersion,
                max_versions = meta.MaxVersions,
                created_time = meta.CreatedAt,
                updated_time = meta.UpdatedAt,
            },
        });
});

app.MapDelete("/v1/secret/data/{*path}", async (string? path, HttpContext http) =>
{
    path ??= string.Empty;
    var (_, err) = await Authorize(http, Capability.Delete);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var meta = await engine.Kv.GetMetadataAsync(path);
    if (meta is null) return Results.NotFound(new { errors = new[] { "not found" } });

    await engine.Kv.SoftDeleteAsync(path, new[] { meta.CurrentVersion });
    await SafeAudit(http, "delete", path);
    return Results.NoContent();
});

// --- sys: policies (sudo) ---

app.MapGet("/v1/sys/policies", async (HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    return Results.Json(new { data = new { keys = await engine.Store.ListPoliciesAsync() } });
});

app.MapGet("/v1/sys/policy/{name}", async (string name, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    var p = await engine.Store.GetPolicyAsync(name);
    return p is null
        ? Results.NotFound(new { errors = new[] { "not found" } })
        : Results.Json(new
        {
            data = new
            {
                name = p.Name,
                rules = p.Rules.Select(r => new { path = r.PathPattern, capabilities = CapsToStrings(r.Capabilities) }),
            },
        });
});

app.MapPost("/v1/sys/policy/{name}", async (string name, PolicyWriteRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    var rules = (req.Rules ?? new List<PolicyRuleDto>())
        .Select(r => new PolicyRule { PathPattern = r.Path, Capabilities = ParseCapabilities(r.Capabilities) })
        .ToList();
    await engine.Store.PutPolicyAsync(new Policy { Name = name, Rules = rules });
    return Results.NoContent();
});

// --- sys: explain access (the differentiator) + audit verify (sudo) ---

app.MapPost("/v1/sys/policy/explain", async (ExplainRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (string.IsNullOrEmpty(req.Path) || string.IsNullOrEmpty(req.Capability))
        return Results.BadRequest(new { errors = new[] { "path and capability are required" } });

    var policies = new List<Policy>();
    foreach (var n in req.Policies ?? new List<string>())
        if (await engine.Store.GetPolicyAsync(n) is { } p) policies.Add(p);

    var decision = engine.Policy.Evaluate(policies, req.Path, ParseCapability(req.Capability));
    return Results.Json(new
    {
        allowed = decision.Allowed,
        path = decision.Path,
        capability = req.Capability,
        winning_policy = decision.WinningPolicy,
        winning_rule = decision.WinningRule?.PathPattern,
        explanation = decision.Explanation,
    });
});

app.MapPost("/v1/sys/audit/verify", async (HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();
    var r = await engine.Audit.VerifyAsync();
    return Results.Json(new { valid = r.Valid, first_broken_seq = r.FirstBrokenSeq, checked_count = r.Checked });
});

// --- cubbyhole (per-token private store, mounted engine) ---

app.MapGet("/v1/cubbyhole/{*path}", async (string? path, HttpContext http) =>
{
    path ??= string.Empty;
    var (tok, err) = await Authorize(http, Capability.Read);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var r = await engine.Router.RouteAsync(EngineOp.Read, "cubbyhole/" + path, null, tok!.Accessor);
    return r.NotFound ? Results.NotFound(new { errors = new[] { "not found" } }) : Results.Json(new { data = r.Data });
});

app.MapPost("/v1/cubbyhole/{*path}", async (string? path, KvWriteRequest req, HttpContext http) =>
{
    path ??= string.Empty;
    var (tok, err) = await Authorize(http, Capability.Update);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    await engine.Router.RouteAsync(EngineOp.Write, "cubbyhole/" + path, req.Data ?? new Dictionary<string, string>(), tok!.Accessor);
    return Results.NoContent();
});

app.MapDelete("/v1/cubbyhole/{*path}", async (string? path, HttpContext http) =>
{
    path ??= string.Empty;
    var (tok, err) = await Authorize(http, Capability.Delete);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    await engine.Router.RouteAsync(EngineOp.Delete, "cubbyhole/" + path, null, tok!.Accessor);
    return Results.NoContent();
});

// --- identity (entities / groups / aliases) ---

app.MapPost("/v1/identity/entity", async (EntityCreateRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();
    if (string.IsNullOrEmpty(req.Name)) return Results.BadRequest(new { errors = new[] { "name is required" } });

    var id = await engine.Identity.CreateEntityAsync(req.Name, req.Policies ?? new List<string>(), req.Metadata);
    return Results.Json(new { data = new { id } });
});

app.MapGet("/v1/identity/entity/{id}", async (string id, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    var e = await engine.Identity.GetEntityAsync(id);
    return e is null
        ? Results.NotFound(new { errors = new[] { "entity not found" } })
        : Results.Json(new { data = new { id = e.Id, name = e.Name, policies = e.Policies, metadata = e.Metadata } });
});

app.MapPost("/v1/identity/group", async (GroupCreateRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (string.IsNullOrEmpty(req.Name)) return Results.BadRequest(new { errors = new[] { "name is required" } });

    await engine.Identity.CreateGroupAsync(req.Name, req.Policies ?? new List<string>(), req.MemberEntityIds ?? new List<string>());
    return Results.NoContent();
});

app.MapPost("/v1/identity/alias", async (AliasCreateRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (string.IsNullOrEmpty(req.Principal) || string.IsNullOrEmpty(req.EntityId))
        return Results.BadRequest(new { errors = new[] { "principal and entity_id are required" } });

    await engine.Identity.CreateAliasAsync(req.Principal, req.EntityId);
    return Results.NoContent();
});

// --- dynamic database secrets + leases ---

app.MapGet("/v1/database/creds/{role}", async (string role, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Read);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var r = await engine.Router.RouteAsync(EngineOp.Read, "database/creds/" + role);
    return r.NotFound ? Results.NotFound(new { errors = new[] { "database role not configured" } }) : Results.Json(new { data = r.Data });
});

app.MapGet("/v1/sys/leases", async (HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    return Results.Json(new { data = new { keys = await engine.Leases.ListAsync() } });
});

app.MapPost("/v1/sys/leases/revoke", async (LeaseRevokeRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (string.IsNullOrEmpty(req.LeaseId)) return Results.BadRequest(new { errors = new[] { "lease_id is required" } });

    return await engine.Leases.RevokeAsync(req.LeaseId)
        ? Results.NoContent()
        : Results.NotFound(new { errors = new[] { "lease not found" } });
});

// --- AppRole machine auth ---

app.MapPost("/v1/auth/approle/role/{name}", async (string name, AppRoleCreateRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var roleId = await engine.AppRole.CreateRoleAsync(
        name,
        req.TokenPolicies ?? new List<string>(),
        TimeSpan.FromSeconds(req.TokenTtl ?? 3600),
        TimeSpan.FromSeconds(req.SecretIdTtl ?? 0),
        req.SecretIdNumUses ?? 0);
    return Results.Json(new { data = new { role_id = roleId } });
});

app.MapGet("/v1/auth/approle/role/{name}/role-id", async (string name, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    var id = await engine.AppRole.GetRoleIdAsync(name);
    return id is null ? Results.NotFound(new { errors = new[] { "role not found" } }) : Results.Json(new { data = new { role_id = id } });
});

app.MapPost("/v1/auth/approle/role/{name}/secret-id", async (string name, SecretIdRequest? req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var secretId = await engine.AppRole.GenerateSecretIdAsync(name);
    if (secretId is null) return Results.NotFound(new { errors = new[] { "role not found" } });

    // Optional secure delivery: response-wrap the secret_id (secret-zero, ADR-0011).
    if (req?.WrapTtl is > 0)
    {
        var token = await engine.Wrapping.WrapAsync(
            new Dictionary<string, string> { ["secret_id"] = secretId }, TimeSpan.FromSeconds(req.WrapTtl.Value));
        return Results.Json(new { wrap_info = new { token, ttl = req.WrapTtl.Value } });
    }
    return Results.Json(new { data = new { secret_id = secretId } });
});

app.MapPost("/v1/auth/approle/login", async (AppRoleLoginRequest req) =>
{
    if (engine.Keys.IsSealed) return Sealed();
    if (string.IsNullOrEmpty(req.RoleId) || string.IsNullOrEmpty(req.SecretId))
        return Results.BadRequest(new { errors = new[] { "role_id and secret_id are required" } });

    var result = await engine.AppRole.LoginAsync(req.RoleId, req.SecretId);
    return result is null
        ? Results.Json(new { errors = new[] { "invalid role or secret id" } }, statusCode: 400)
        : Results.Json(new
        {
            auth = new
            {
                client_token = result.Value.Token,
                accessor = result.Value.Info.Accessor,
                policies = result.Value.Info.Policies,
            },
        });
});

// --- transit (encryption-as-a-service; never stores plaintext) ---

app.MapPost("/v1/transit/keys/{name}", async (string name, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Update);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();
    await engine.Router.RouteAsync(EngineOp.Write, "transit/keys/" + name, null);
    return Results.NoContent();
});

app.MapPost("/v1/transit/encrypt/{name}", async (string name, TransitEncryptRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Update);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();
    if (string.IsNullOrEmpty(req.Plaintext)) return Results.BadRequest(new { errors = new[] { "plaintext (base64) is required" } });

    var r = await engine.Router.RouteAsync(EngineOp.Write, "transit/encrypt/" + name,
        new Dictionary<string, string> { ["plaintext"] = req.Plaintext });
    return r.NotFound ? Results.NotFound(new { errors = new[] { "key not found" } }) : Results.Json(new { data = r.Data });
});

app.MapPost("/v1/transit/decrypt/{name}", async (string name, TransitDecryptRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Update);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();
    if (string.IsNullOrEmpty(req.Ciphertext)) return Results.BadRequest(new { errors = new[] { "ciphertext is required" } });

    var r = await engine.Router.RouteAsync(EngineOp.Write, "transit/decrypt/" + name,
        new Dictionary<string, string> { ["ciphertext"] = req.Ciphertext });
    return r.NotFound ? Results.NotFound(new { errors = new[] { "key not found or bad ciphertext" } }) : Results.Json(new { data = r.Data });
});

// --- response wrapping ---

app.MapPost("/v1/sys/wrapping/wrap", async (WrapRequest req, HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Update);
    if (err is not null) return err;
    if (engine.Keys.IsSealed) return Sealed();

    var ttl = TimeSpan.FromSeconds(req.TtlSeconds is > 0 ? req.TtlSeconds.Value : 300);
    var token = await engine.Wrapping.WrapAsync(req.Data ?? new Dictionary<string, string>(), ttl);
    return Results.Json(new { wrap_info = new { token, ttl = (int)ttl.TotalSeconds } });
});

app.MapPost("/v1/sys/wrapping/unwrap", async (UnwrapRequest req, HttpContext http) =>
{
    // The wrapping token itself is the credential — from the body or the X-Vault-Token header.
    var wrappingToken = !string.IsNullOrEmpty(req.Token)
        ? req.Token
        : http.Request.Headers["X-Vault-Token"].FirstOrDefault();
    if (string.IsNullOrEmpty(wrappingToken)) return Results.BadRequest(new { errors = new[] { "wrapping token required" } });
    if (engine.Keys.IsSealed) return Sealed();

    var data = await engine.Wrapping.UnwrapAsync(wrappingToken);
    return data is null
        ? Results.BadRequest(new { errors = new[] { "invalid or already-used wrapping token" } })
        : Results.Json(new { data });
});

app.MapPost("/v1/sys/wrapping/lookup", async (UnwrapRequest req, HttpContext http) =>
{
    var wrappingToken = !string.IsNullOrEmpty(req.Token)
        ? req.Token
        : http.Request.Headers["X-Vault-Token"].FirstOrDefault();
    if (string.IsNullOrEmpty(wrappingToken)) return Results.BadRequest(new { errors = new[] { "wrapping token required" } });

    return Results.Json(new { valid = await engine.Wrapping.LookupAsync(wrappingToken) });
});

// --- sys: mounted engines (sudo) ---

app.MapGet("/v1/sys/mounts", async (HttpContext http) =>
{
    var (_, err) = await Authorize(http, Capability.Sudo);
    if (err is not null) return err;
    var mounts = engine.Router.Mounts
        .Select(p => new { path = p, type = engine.Router.EngineFor(p)?.Type ?? "unknown" });
    return Results.Json(new { data = new { mounts } });
});

// SPA fallback: any /ui/* route that isn't a file returns the WASM host page so client-side
// routing works on refresh/deep-link. API routes (/v1/*) are unaffected.
app.MapFallbackToFile("/ui/{*path:nonfile}", "ui/index.html");

app.Run();

// --- helpers (local functions; hoisted within the top-level scope) ---

IResult Sealed() => Results.Json(new { errors = new[] { "stashr is sealed" } }, statusCode: 503);

async Task<(TokenInfo? Token, IResult? Error)> Authorize(HttpContext http, Capability capability)
{
    var token = await Bootstrap.AuthenticateAsync(http, engine);
    if (token is null)
        return (null, Results.Json(new { errors = new[] { "missing or invalid token" } }, statusCode: 401));

    var policies = new List<Policy>();
    foreach (var name in token.Policies)
        if (await engine.Store.GetPolicyAsync(name) is { } p) policies.Add(p);

    var path = http.Request.Path.Value!.TrimStart('/');
    if (path.StartsWith("v1/", StringComparison.Ordinal)) path = path[3..];

    var decision = engine.Policy.Evaluate(policies, path, capability);
    return decision.Allowed
        ? (token, null)
        : (token, Results.Json(new { errors = new[] { "permission denied" }, explanation = decision.Explanation }, statusCode: 403));
}

async Task SafeAudit(HttpContext http, string op, string path)
{
    try
    {
        await engine.Audit.AppendAsync(new AuditEntry
        {
            Seq = 0,
            RequestId = http.TraceIdentifier,
            Type = "response",
            Operation = op,
            Path = path,
            RemoteAddr = http.Connection.RemoteIpAddress?.ToString(),
            Decision = "granted",
        });
    }
    catch
    {
        // 4a/4b: best-effort; fail-closed required sinks arrive with audit-device config (ADR-0005).
    }
}

static Capability ParseCapability(string? s) => s?.Trim().ToLowerInvariant() switch
{
    "create" => Capability.Create,
    "read" => Capability.Read,
    "update" or "write" => Capability.Update,
    "delete" => Capability.Delete,
    "list" => Capability.List,
    "sudo" => Capability.Sudo,
    "deny" => Capability.Deny,
    _ => Capability.None,
};

static Capability ParseCapabilities(List<string>? caps)
{
    var result = Capability.None;
    foreach (var c in caps ?? new List<string>()) result |= ParseCapability(c);
    return result;
}

static string[] CapsToStrings(Capability caps)
{
    var list = new List<string>();
    foreach (var v in Enum.GetValues<Capability>())
        if (v != Capability.None && caps.HasFlag(v)) list.Add(v.ToString().ToLowerInvariant());
    return list.ToArray();
}
