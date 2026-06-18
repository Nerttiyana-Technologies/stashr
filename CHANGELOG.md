# Changelog

All notable changes to stashr are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/).

## [0.9.0] — 2026-06-18

First public release. Feature-complete for near-Vault parity; **pre-independent-audit**.
`1.0.0` is reserved for the first release after an independent security assessment.

### Added

- **Secrets engines:** KV v2 (versioned, soft-delete, metadata), Transit
  (encryption-as-a-service), dynamic PostgreSQL credentials (lease-bound, auto-revoked),
  Cubbyhole (per-token private store), Identity (entities/groups/aliases).
- **Authentication & authorization:** token (service/batch) auth, AppRole machine auth with
  response-wrapped secret-id delivery, deny-by-default path policies, and an `explain-access`
  endpoint that reports *why* a request is allowed or denied.
- **Response wrapping** for secure secret-zero delivery.
- **Sealing:** Shamir split-knowledge seal/unseal and pluggable auto-unseal (recovery keys).
- **High availability:** PostgreSQL advisory-lock leader election with fencing epoch and
  fail-closed self-seal on loss of leadership.
- **Tamper-evident audit log:** HMAC hash-chain with genesis binding and truncation defense,
  plus an integrity-verification endpoint.
- **Cryptography & hardening:** AES-256-GCM envelope encryption (master → DEK), secure
  (pinned/zeroed) memory, FIPS detection (fail-closed when required), core-dump disabling.
  Engine data is encrypted at rest under the DEK, including mounted-engine storage.
- **Transport:** TLS and mutual-TLS for the server.
- **Web UI:** a built-in Blazor WebAssembly admin console served at `/ui` with light & dark
  themes — init/unseal, KV browser, policies + explain-access, AppRole credentials, Transit,
  leases, mounts, and audit verification.
- **Developer surface:** `Stashr.Client` SDK, an `AddStashr()` `IConfiguration` provider, a
  `ConfigurationBuilder` for .NET Framework 4.8 `app.config`/`web.config`, and the `stashr` CLI.
- **Storage backends:** PostgreSQL (production) and in-memory (dev/test).
- **Packaging:** multi-stage Docker image (non-root), single-node and two-node HA Compose
  stacks, the CLI as a `dotnet` tool, and self-contained single-binary publish.
- **Quality gates:** NetArchTest architectural boundary tests, Testcontainers integration
  tests, and GitHub Actions CI on Ubuntu and Windows.

### Security notes

- This is a **pre-audit** release. See [SECURITY.md](SECURITY.md) and [COMPLIANCE.md](COMPLIANCE.md).
- FIPS-validated cryptography is **inherited from a FIPS-configured host OS**; stashr is not
  itself CMVP-validated.

[0.9.0]: https://github.com/Nerttiyana-Technologies/stashr/releases/tag/v0.9.0
