# Releasing stashr

The repeatable checklist for cutting a public release. `0.9.0` is the first public release
(pre-audit); `1.0.0` is reserved for the first release after an independent security assessment.

Steps marked **(you)** must be done by a maintainer with the right credentials/permissions —
they involve pushing tags, publishing artifacts, entering tokens, or changing repository access.

## 1. Pre-flight

- [ ] Confirm the version everywhere is consistent: `CHANGELOG.md`, `docs/release-notes/v<version>.md`,
      the README status badge, the UI footer (`src/Stashr.Ui` shows `v0.9.0`), and the
      `/v1/sys/health` version string.
- [ ] Confirm `internal/` is **not** tracked (design docs stay private): `git status` shows nothing
      under `internal/`, and `.gitignore` still excludes it.
- [ ] No secrets, tokens, or `*.pfx` are staged: `git status` is clean of `stashr.pfx` and dev certs.
- [ ] `LICENSE` (Apache-2.0) and `NOTICE` are present and current.

## 2. Build & test (green gate)

```bash
./scripts/init-solution.sh
dotnet build            # warnings are errors — must be clean
dotnet test             # unit + architecture tests
```

- [ ] `dotnet build` clean, `dotnet test` green.
- [ ] Integration tests pass with a Docker daemon running (Docker Desktop / **OrbStack** / Colima)
      for Testcontainers (PostgreSQL + dynamic DB creds).

## 3. Web UI acceptance

- [ ] Run the server (`dotnet run --project src/Stashr.Server` or `docker compose up --build`),
      open `/ui`, and work through `docs/testing/ui-test-plan-0.9.0.md`.
- [ ] Repeat the smoke tests in **both light and dark** themes and a second browser.
- [ ] All sections Pass (or every Fail triaged and accepted). This is the UI release gate.

## 4. Packaging verification

```bash
docker build -t stashr:<version> .              # multi-stage image builds, runs non-root
docker compose up --build                        # /ui reachable; root token printed
dotnet pack src/Stashr.Cli -c Release            # produces the dotnet tool package
dotnet publish src/Stashr.Server -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -o ./publish          # self-contained binary runs
```

- [ ] Docker image builds and serves the API + `/ui`.
- [ ] `stashr` CLI installs and runs (`dotnet tool install --global --add-source … Stashr.Cli`).
- [ ] Self-contained binary starts on a host without the .NET runtime.

## 5. Docs final check

- [ ] `README.md` renders with the header banner and correct quickstart commands.
- [ ] `COMPLIANCE.md` + `docs/compliance/control-evidence-matrix.md` are accurate (pre-audit,
      inherited-FIPS framing — never "FIPS certified" / "FedRAMP compliant").
- [ ] `SECURITY.md` reporting contact is correct.
- [ ] `CHANGELOG.md` has the release entry with the right date.
- [ ] `docs/release-notes/v<version>.md` reads well (this becomes the GitHub Release body).

## 6. Make the repository public — **(you)**

- [ ] In GitHub repo **Settings → General → Danger Zone → Change visibility**, set to public.
      (Access-control changes must be done by a maintainer in GitHub.)
- [ ] Enable **Private vulnerability reporting** (Settings → Security) so `SECURITY.md` works.

## 7. Tag & GitHub Release — **(you)**

```bash
git add -A && git commit -m "Release v0.9.0"
git tag -a v0.9.0 -m "stashr v0.9.0"
git push origin main
git push origin v0.9.0
```

- [ ] Create the GitHub Release from tag `v0.9.0`, titled `stashr v0.9.0`, body from
      `docs/release-notes/v0.9.0.md`. Mark as **pre-release** if you want to signal pre-audit status.
- [ ] (Optional) Attach the self-contained binaries (linux-x64, osx-arm64, win-x64) as assets.

## 8. Publish artifacts

**Automated (preferred).** Pushing the `v*` tag triggers `.github/workflows/release.yml`, which:
publishes the four NuGet packages (`Stashr.Client`, `Stashr.Configuration`,
`Stashr.Configuration.Legacy`, `Stashr.Cli`) to nuget.org, pushes the Docker image to
`ghcr.io/<owner>/stashr:<version>` + `:latest`, and attaches self-contained server binaries
(linux-x64 / win-x64 / osx-arm64) to the GitHub Release.

- [ ] **One-time prerequisite — (you): NuGet Trusted Publishing (OIDC — no stored API key).**
      On nuget.org → your username → **Trusted Publishing**, add a policy with
      Repository Owner `Nerttiyana-Technologies`, Repository `stashr`, Workflow File `release.yml`
      (file name only, no path). Then add a repo secret **`NUGET_USER`** = your nuget.org username
      (profile name, *not* email). The workflow mints a short-lived key at run time via
      `NuGet/login@v1`; GHCR uses the built-in `GITHUB_TOKEN` (no secret needed).
      > Private repos: the policy stays "pending" for 7 days until the first successful publish locks it in.
- [ ] After tagging, watch the **release** workflow succeed; verify the packages appear on nuget.org
      and the image on GHCR.

**Manual fallback — (you):** API keys are discouraged for automation but still work for one-off
command-line pushes (generate one at nuget.org → API keys → "force API keys").

```bash
VERSION=0.9.0
for p in Stashr.Client Stashr.Configuration Stashr.Configuration.Legacy Stashr.Cli; do
  dotnet pack "src/$p/$p.csproj" -c Release -p:Version=$VERSION -o artifacts
done
dotnet nuget push "artifacts/*.nupkg" --api-key <KEY> --source https://api.nuget.org/v3/index.json --skip-duplicate
```

Then consumers install with `dotnet add package Stashr.Client`, `dotnet tool install --global Stashr.Cli`,
or `docker pull ghcr.io/<owner>/stashr:<version>` — no source checkout required.

## 9. Post-release

- [ ] Announce (repo README badge, any channels).
- [ ] Open tracking issues for the **independent security audit** (the gate to `1.0.0`) and the
      **OpenSSF Best Practices Badge**.
- [ ] Start the `[Unreleased]` section in `CHANGELOG.md` for the next cycle.

---

> A maintainer cannot, and tooling should not, enter registry/NuGet credentials or change repo
> access on anyone's behalf — those steps are intentionally manual.
