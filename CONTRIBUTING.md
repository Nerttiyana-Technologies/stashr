# Contributing to stashr

Thanks for your interest in stashr. It's an open-source, fully .NET secrets engine, and
contributions — code, docs, tests, bug reports — are welcome.

## Ground rules

- **Security first.** stashr stores secrets. Correctness and clarity beat cleverness. If a
  change touches crypto, sealing, policy, or audit, explain the reasoning and add tests.
- **No plaintext secrets, ever** — not in code, tests, fixtures, logs, or commit history.
- **Keep the layering intact.** `Stashr.Core` stays infrastructure-free; the engine depends on
  storage *abstractions*, not concrete providers. These rules are enforced by the architecture
  tests in `test/Stashr.Architecture.Tests` — run them before you push.

## Getting set up

You need the **.NET 10 SDK** (`global.json` pins the version). Then:

```bash
./scripts/init-solution.sh   # assemble the solution
dotnet build
dotnet test
```

Integration tests (PostgreSQL, dynamic DB creds) need a Docker-compatible daemon
(Docker Desktop, OrbStack, or Colima) for Testcontainers. See [BUILDING.md](BUILDING.md) for the
full developer loop, packaging, and the two-node HA demo.

## Making a change

1. Open an issue first for anything non-trivial, so we can agree on the approach.
2. Branch from `main`.
3. Keep PRs focused. Include tests for new behavior and update docs/ADRs when design changes.
4. Significant design decisions are captured as ADRs — propose one when a change alters
   architecture, security properties, or public contracts.
5. Ensure `dotnet build` is clean (warnings are errors) and `dotnet test` is green.

## Code style

- C# latest, nullable enabled, warnings-as-errors. Match the surrounding style.
- Prefer small, well-named types and pure functions in `Core`/`Crypto`.
- Public APIs get XML docs; security-relevant code gets a comment explaining the *why*.

## Reporting security issues

Please **do not** open a public issue for vulnerabilities. Follow [SECURITY.md](SECURITY.md).

## License

By contributing, you agree your contributions are licensed under the
[Apache-2.0](LICENSE) license.
