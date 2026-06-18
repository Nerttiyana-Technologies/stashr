<p align="center">
  <img alt="stashr — the .NET-native secrets engine" src="https://raw.githubusercontent.com/Nerttiyana-Technologies/stashr/main/assets/logo/stashr-banner.png" width="100%">
</p>

<p align="center">
  <strong>A fully .NET, open-source secrets engine.</strong><br>
  Vault-compatible API · KV v2 · Transit · dynamic database creds · HA + auto-unseal · built-in web UI.
</p>

<p align="center">
  <img alt="License" src="https://img.shields.io/badge/license-Apache--2.0-blue">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10%20%7C%208%20%7C%20Framework%204.8-512BD4">
  <img alt="Status" src="https://img.shields.io/badge/release-0.9.0%20(pre--audit)-orange">
</p>

---

> ⚠️ **Pre-audit release (v0.9.0).** stashr has **not** yet had an independent third-party
> security audit. It is suitable for evaluation, internal tooling, and non-critical workloads.
> Do not store crown-jewel production secrets in stashr until the audit completes. `1.0.0` is
> reserved for the first post-audit release. See [Compliance & Security Posture](COMPLIANCE.md).

## Why stashr

If your stack is .NET, your secrets manager probably isn't. For a .NET shop that means an opaque dependency you can't read, debug, extend, or audit in your own language.

stashr is **secrets management built natively for .NET**:

- **One language, top to bottom.** Engine, server, SDK, CLI, and web UI are all .NET. Your
  team can read the source, step through it in a debugger, and contribute.
- **Native integration.** A first-class SDK and an `AddStashr()` configuration provider mean
  secrets flow into `IConfiguration` with no plaintext in `appsettings.json`.
- **Drop-in for legacy too.** A classic `ConfigurationBuilder` resolves `app.config` /
  `web.config` values for .NET Framework 4.8 apps — the migration path off plaintext config.
- **Vault-compatible HTTP API.** The `/v1` surface mirrors Vault's shapes, so existing tooling
  and mental models carry over.

## Features

- **KV v2** versioned secrets, with soft-delete and metadata.
- **Transit** encryption-as-a-service — encrypt/decrypt without ever storing plaintext.
- **Dynamic database secrets** — short-lived PostgreSQL credentials, auto-revoked on lease expiry.
- **Cubbyhole** per-token private storage and **response wrapping** for secure secret-zero delivery.
- **Identity** entities, groups, and aliases.
- **AppRole** machine authentication; **token** service/batch auth.
- **Deny-by-default policies** with a unique **explain-access** endpoint ("*why* was this allowed/denied?").
- **Tamper-evident audit** — HMAC hash-chained log with genesis binding and truncation defense.
- **Seal/unseal** via Shamir split-knowledge; **auto-unseal** via a pluggable seal provider.
- **High availability** — PostgreSQL leader election with fencing and fail-closed self-seal.
- **TLS / mTLS**, FIPS-aware crypto (inherited from the host OS), and process hardening.
- **Built-in web UI** (Blazor WebAssembly) served at `/ui` — light & dark themes.
- **Storage backends:** PostgreSQL (production) and in-memory (dev/test).

## Install

stashr ships as ready-to-run artifacts — you don't need to clone or build it.

**Run the server**

```bash
docker pull ghcr.io/nerttiyana-technologies/stashr:0.9.0
docker run -d -p 8200:8080 ghcr.io/nerttiyana-technologies/stashr:0.9.0
```

Or download a self-contained server binary (linux-x64 / win-x64 / osx-arm64) from the
[Releases](https://github.com/Nerttiyana-Technologies/stashr/releases) page — no .NET runtime required.

**Operator CLI**

```bash
dotnet tool install --global Stashr.Cli
stashr status
```

**Use it from your app** — add the NuGet packages:

| Package | Frameworks | Use |
|---------|-----------|-----|
| [`Stashr.Client`](https://www.nuget.org/packages/Stashr.Client) | net10 · net8 · netstandard2.0 | SDK over the HTTP API |
| [`Stashr.Configuration`](https://www.nuget.org/packages/Stashr.Configuration) | net10 · net8 · netstandard2.0 | `AddStashr()` config provider |
| [`Stashr.Configuration.Legacy`](https://www.nuget.org/packages/Stashr.Configuration.Legacy) | net48 | `web.config` / `app.config` ConfigurationBuilder |

```bash
dotnet add package Stashr.Client
dotnet add package Stashr.Configuration
```

Building from source is only needed for contributors — see [BUILDING.md](BUILDING.md).

## Quickstart

### Docker (5 minutes)

```bash
docker compose up --build          # PostgreSQL + stashr in dev mode
# the console logs a one-time root token; the API is on http://localhost:8080
# open the web UI at http://localhost:8080/ui
```

### From source

```bash
dotnet run --project src/Stashr.Server     # dev mode: auto-init + unseal, prints a root token
# API on http://localhost:5000, web UI at http://localhost:5000/ui
```

```bash
TOKEN=<root token from the log>
BASE=http://localhost:5000

curl -H "X-Vault-Token: $TOKEN" -X POST $BASE/v1/secret/data/app/db \
     -H "Content-Type: application/json" -d '{"data":{"password":"p@ss","host":"db1"}}'
curl -H "X-Vault-Token: $TOKEN" $BASE/v1/secret/data/app/db
```

See [BUILDING.md](BUILDING.md) for the full build, test, packaging, and HA-demo instructions.

## Deployment

Dev mode (in-memory storage, auto-init/unseal) is for evaluation only. A production deployment
uses PostgreSQL storage, TLS, and a real initialize/unseal — optionally with auto-unseal and HA.

### 1. Run it

```bash
docker run -d --name stashr -p 8200:8080 \
  -e Stashr__Storage=Postgres \
  -e Stashr__Postgres__ConnectionString="Host=db;Database=stashr;Username=stashr;Password=…" \
  -e Stashr__RequireFips=false \
  stashr:0.9.0
```

Configuration comes from `appsettings.json` or environment variables (nested keys use `__`).
The single-binary and `dotnet` deployments read the same settings.

### 2. Configuration

| Setting | Env var | Purpose |
|---------|---------|---------|
| `Stashr:Storage` | `Stashr__Storage` | `Postgres` (production) or `InMemory` (dev/test) |
| `Stashr:Postgres:ConnectionString` | `Stashr__Postgres__ConnectionString` | Npgsql connection string for the storage backend |
| `Stashr:DevMode` | `Stashr__DevMode` | Auto-init + unseal for local dev. **Never `true` in production.** |
| `Stashr:RequireFips` | `Stashr__RequireFips` | Fail-closed unless a FIPS-validated module is active (set `true` on a FIPS host) |
| `Stashr:AutoUnseal:Key` | `Stashr__AutoUnseal__Key` | Base64 32-byte key for auto-unseal (inject from your KMS/secret store) |
| `Stashr:Ha:Enabled` | `Stashr__Ha__Enabled` | Active/standby high availability (requires PostgreSQL) |
| `Stashr:Tls:CertPath` / `Stashr:Tls:CertPassword` | `Stashr__Tls__…` | PKCS#12 cert for HTTPS / mTLS (see [BUILDING.md](BUILDING.md)) |

### 3. Initialize & unseal (first run)

Open the web UI at `https://<host>:8200/ui` and follow the initialize screen, or use the API:

```bash
# initialize once — SAVE the root token and unseal keys it returns
curl -k -X POST https://<host>:8200/v1/sys/init \
  -H "Content-Type: application/json" -d '{"secret_shares":5,"secret_threshold":3}'

# submit unseal keys until the threshold is met
curl -k -X POST https://<host>:8200/v1/sys/unseal \
  -H "Content-Type: application/json" -d '{"key":"<unseal-key-1>"}'
```

With `Stashr__AutoUnseal__Key` set, a node unseals itself on start after the one-time init —
no manual unseal on restart.

### 4. High availability

Run two or more nodes against the **same PostgreSQL** with `Stashr__Ha__Enabled=true` and a shared
`Stashr__AutoUnseal__Key`. One node is active; a standby is promoted automatically on failure
(leader election + fencing + fail-closed self-seal). A working two-node demo is in
[`docker-compose.ha.yml`](docker-compose.ha.yml).

### 5. TLS & hardening

Terminate TLS at stashr with a PKCS#12 certificate (and optional mutual TLS for clients). On a
FIPS-configured host (RHEL OpenSSL / Windows CNG), set `Stashr__RequireFips=true` so the engine
refuses to start without a validated module. The full production checklist lives in
[RELEASING.md](RELEASING.md) and [COMPLIANCE.md](COMPLIANCE.md).

> Pre-audit: until the independent security audit completes, avoid storing crown-jewel production
> secrets (see [SECURITY.md](SECURITY.md)).

## Use it from .NET

```csharp
// SDK
var client = new StashrClient(new Uri("https://stashr.internal:8200"), token);
var secret = await client.GetSecretAsync("app/db");

// Or bind straight into IConfiguration — no secrets in appsettings.json
builder.Configuration.AddStashr(options =>
{
    options.Address = new Uri("https://stashr.internal:8200");
    options.Token   = Environment.GetEnvironmentVariable("STASHR_TOKEN");
    options.Path    = "app/db";
});
```

For .NET Framework apps, register `Stashr.Configuration.Legacy.StashrConfigBuilder` in
`web.config` to resolve `appSettings` / `connectionStrings` from stashr.

## Web UI

stashr ships an admin console (Blazor WebAssembly) inside the server binary at `/ui`:
initialize/unseal, browse and edit KV secrets, manage policies with the explain-access tester,
issue AppRole credentials, use Transit, inspect leases and mounts, and verify the audit chain —
with light and dark themes. It calls the same public `/v1` API as the CLI and SDK; the server
stays stateless.

## Architecture

stashr is a layered .NET solution: a dependency-light `Stashr.Core` (contracts + domain),
`Stashr.Crypto` (Shamir, AES-256-GCM envelope encryption, secure memory), `Stashr.Engine`
(seal/unseal, KV, policy, audit, tokens, AppRole, mounts, transit, cubbyhole, leases, identity),
pluggable storage (`InMemory`, `Postgres`), and the ASP.NET `Stashr.Server` host. Architectural
boundaries are enforced as build-gate tests. Design decisions are documented as ADRs.

## Compliance, honestly

stashr is a security product, so we state our posture precisely. It is **designed to** satisfy
NIST 800-53 / SOC 2 / PCI-DSS / HIPAA control requirements and **inherits FIPS-validated
cryptography from the host OS** on a FIPS-configured platform. It is **not** FIPS-certified, not
FedRAMP-authorized, and not yet independently audited. Read the full, precise statement in
[COMPLIANCE.md](COMPLIANCE.md) and the [Control Evidence Matrix](docs/compliance/control-evidence-matrix.md).

## Project status

`0.9.0` is the first public release: feature-complete for near-Vault parity, pre-independent-audit.
`1.0.0` is reserved for the first release after an independent security assessment.

See the [v0.9.0 release notes](docs/release-notes/v0.9.0.md) and the [CHANGELOG](CHANGELOG.md).

## Contributing & security

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). To report a vulnerability,
please follow [SECURITY.md](SECURITY.md) (do not open a public issue for security reports).

## License

[Apache-2.0](LICENSE). © Nerttiyana Technologies.
