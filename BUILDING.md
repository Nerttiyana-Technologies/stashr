# Building stashr

Requires the **.NET 10 SDK** (`global.json` pins `10.0.301`, roll-forward `latestPatch`).
Builds with Rider or the CLI. Develop on macOS/arm64; CI verifies FIPS + SQL Server on
RHEL/Windows (internal ADR-0014).

**Target frameworks (multi-target):** consumer-facing libraries build for
`net10.0;net8.0;netstandard2.0` (the netstandard2.0 leg gives .NET Framework 4.8 reach for the
legacy ConfigurationBuilder, ADR-0013); server/crypto projects build for `net10.0;net8.0` only
(APIs like `AesGcm` aren't in netstandard2.0).

## First-time setup

```bash
# from the repo root — creates stashr.sln and adds existing projects
./scripts/init-solution.sh

# restore + build everything
dotnet build

# run the unit tests
dotnet test
```

`init-solution.sh` is re-run-safe: it recreates `stashr.sln` and adds whichever projects
exist, so as new layers land you just run it again.

## What builds today (Layer 1)

| Project | Target(s) | Purpose |
|---------|-----------|---------|
| `src/Stashr.Core` | `net10.0;net8.0;netstandard2.0` | Contracts + domain models, no I/O (ADR-0001/0007/0008) |
| `src/Stashr.Crypto` | `net10.0;net8.0` | FIPS-aware provider, Shamir, envelope, secure memory (ADR-0004/0015) |
| `src/Stashr.Storage.InMemory` | `net10.0;net8.0` | Ephemeral `ISecretStore` for tests + `-dev` mode (ADR-0014) |
| `src/Stashr.Storage.Postgres` | `net10.0;net8.0` | PostgreSQL `ISecretStore` via Npgsql + schema (ADR-0007) |
| `src/Stashr.Engine` | `net10.0;net8.0` | Seal/unseal + envelope, policy + explain, audit, KV v2, tokens, AppRole, mount/router, cubbyhole, transit, wrapping, leases |
| `src/Stashr.Engines.Database` | `net10.0;net8.0` | Dynamic PostgreSQL credentials (short-lived roles, auto-revoked via leases) — separate assembly (Npgsql) |
| `test/Stashr.Engines.Database.Tests` | `net10.0` | Live Testcontainers: generate creds → connect → revoke → can't connect |
| `test/Stashr.Engine.Tests` | `net10.0` | Seal/unseal round-trip, policy precedence, audit chain + tamper detection, KV versioning, token validation |
| `src/Stashr.Server` | `net10.0` | ASP.NET host: Vault-compatible `/v1` API, dev mode, Synthetix DTO mapping (ADR-0002/0003) |
| `src/Stashr.Client` | `net10.0;net8.0;netstandard2.0` | .NET client SDK over the HTTP API (ADR-0013) |
| `src/Stashr.Configuration` | `net10.0;net8.0;netstandard2.0` | `AddStashr()` IConfiguration provider (ADR-0013) |
| `src/Stashr.Cli` | `net10.0` | `stashr` operator CLI (status/operator/kv/audit/policy); packable as a dotnet tool |
| `samples/Stashr.Sample` | `net10.0` | Console app: SDK round-trip + `AddStashr()` config demo |
| `test/Stashr.Architecture.Tests` | `net10.0` | NetArchTest boundary gates: Core stays infra-free, engine uses storage abstractions (ADR-0014) |
| `test/Stashr.Storage.Postgres.Tests` | `net10.0` | Integration tests vs real Postgres (Testcontainers) — validates the SQL layer (ADR-0007) |

The Postgres integration tests need a Docker-compatible daemon (Docker Desktop, **OrbStack**, or
Colima) — Testcontainers spins up a throwaway `postgres:16` container. To run against an existing
local Postgres instead, set `STASHR_TEST_POSTGRES` to a connection string (the tests truncate the
`stashr_*` tables on setup, so point it at a throwaway database).

CI runs on every push/PR (`.github/workflows/ci.yml`): build + test on Ubuntu **and** Windows
(the Windows leg exercises the `SecureMemory` Win32 P/Invoke), plus a vulnerable-package scan.

## Run it (dev mode — 5-minute first run)

```bash
dotnet run --project src/Stashr.Server     # Development env => dev mode auto-init + unseal
# the console prints a one-time root token; copy it.
TOKEN=<root token from the log>
BASE=http://localhost:5000

curl $BASE/v1/sys/seal-status
curl -H "X-Vault-Token: $TOKEN" -X POST $BASE/v1/secret/data/app/db \
     -d '{"data":{"password":"p@ss","host":"db1"}}'
curl -H "X-Vault-Token: $TOKEN" $BASE/v1/secret/data/app/db
```

KV is now **policy-authorized** (the dev root token carries a `root` policy = `*` with all
capabilities, so the calls above still pass). Try the policy tooling:

```bash
H='-H Content-Type:application/json -H X-Vault-Token:'$TOKEN

# write a scoped policy
curl $H -X POST $BASE/v1/sys/policy/app-read \
  -d '{"rules":[{"path":"secret/data/app/*","capabilities":["read","list"]}]}'

# "explain access" — the differentiator: why is this allowed/denied?
curl $H -X POST $BASE/v1/sys/policy/explain \
  -d '{"path":"secret/data/app/db","capability":"read","policies":["app-read"]}'
# => {"allowed":true,...,"explanation":"allowed: rule 'secret/data/app/*' in policy 'app-read' grants Read."}

# verify the audit hash-chain is intact
curl $H -X POST $BASE/v1/sys/audit/verify
# => {"valid":true,"first_broken_seq":null,"checked_count":N}
```

### Operate it with the CLI

```bash
export STASHR_ADDR=http://localhost:5000
export STASHR_TOKEN=<root token>

dotnet run --project src/Stashr.Cli -- status
dotnet run --project src/Stashr.Cli -- kv put app/db password=p@ss host=db1
dotnet run --project src/Stashr.Cli -- kv get app/db
dotnet run --project src/Stashr.Cli -- audit verify
```

(Packable as a dotnet tool, so after `dotnet pack` it installs as just `stashr ...`.)

### Consume it from a .NET app (the SDK)

With the server running (dev mode) and a root token in hand:

```bash
STASHR_TOKEN=<root token> dotnet run --project samples/Stashr.Sample
# wrote sample/demo (version 1)
# read sample/demo back from stashr:
#   hello = world
#   env = dev
```

That's the whole adoption story: reference `Stashr.Client`, point it at the server, read/write
secrets — no secrets in config files.

Production: set `Stashr:DevMode=false`, `Stashr:Storage=Postgres` (+ connection string), and
`Stashr:RequireFips=true` on a FIPS host; then `POST /v1/sys/init` and `/v1/sys/unseal` manually.

## Packaging & distribution (ADR-0014)

Three ways to ship stashr.

**1. Docker.** A multi-stage `Dockerfile` (non-root runtime, ASP.NET 10 base) builds the server image.

```bash
docker build -t stashr:0.9.0 .

# single-node dev stack (Postgres + stashr, dev mode) — API on http://localhost:8080
docker compose up --build

# two-node HA demo (active/standby + auto-unseal) for OrbStack
docker compose -f docker-compose.ha.yml up --build
curl -s -X POST http://localhost:8080/v1/sys/init -H 'Content-Type: application/json' -d '{}'
docker compose -f docker-compose.ha.yml stop stashr-1   # standby is promoted within seconds
```

**2. `dotnet` global tool (the CLI).** `Stashr.Cli` is `PackAsTool`, so it installs as a plain `stashr` command.

```bash
dotnet pack src/Stashr.Cli -c Release
dotnet tool install --global --add-source ./src/Stashr.Cli/bin/Release Stashr.Cli
stashr status
```

**3. Self-contained single binary (the server).** No .NET runtime needed on the target host.

```bash
dotnet publish src/Stashr.Server -c Release -r linux-x64 \
  --self-contained -p:PublishSingleFile=true -o ./publish
# also: osx-arm64, win-x64
./publish/Stashr.Server
```
| `test/Stashr.Crypto.Tests` | `net10.0` | Shamir round-trip, AEAD, envelope, secure-memory |
| `test/Stashr.Storage.Tests` | `net10.0` | KV versioning, CAS, delete lifecycle, policies, tokens, audit |

Expected: `dotnet build` clean, `dotnet test` green (Shamir split/combine, AES-GCM
round-trip + tamper detection, envelope wrap/unwrap).

## Dependencies

Object mapping uses **Synthetix** (`PackageReference`, pinned `0.1.0`) — a source-generated
mapper. It is referenced only by the mapping layers (Storage / Engine / API), never by
`Stashr.Core` or `Stashr.Crypto`, which stay dependency-light (ADR-0014).

## Coming next (layers)

2. Storage — in-memory + PostgreSQL done (ADR-0007). Postgres integration tests need a live DB
   (Docker / Testcontainers) and come later; for now the gate is a clean compile.
3. Engine core — seal/unseal, KV v2, policy + explain, audit hash-chain, tokens (ADR-0002/0005/0006/0008/0010).
4. HTTP API + AppRole + host (`Program.cs`), dev mode (ADR-0002/0003/0006).
5. Client SDK + config providers + CLI (ADR-0013).

> If `dotnet build` reports errors, paste them back — the build environment here has no .NET
> SDK, so your compile is the verification step for each layer.
