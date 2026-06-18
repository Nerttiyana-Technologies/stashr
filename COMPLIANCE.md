# Compliance & Security Posture

stashr is a security product, so we state our compliance posture **precisely**. We would
rather be exact than impressive. This page reflects the current state and the roadmap, and is
updated as the project matures.

> ⚠️ **Current status (honest summary).** stashr is designed to satisfy the control
> requirements of NIST 800-53, SOC 2, PCI-DSS, and HIPAA, and **inherits FIPS-validated
> cryptography from the host OS** when deployed on a FIPS-configured platform (see below). It
> does **not** yet carry an independent third-party audit (SOC 2 Type II / ISO 27001) or a
> FedRAMP authorization. A formal independent assessment is on the roadmap. Until then, treat
> the indicators below as evidence of *structural alignment with industry baselines*, not as a
> substitute for a third-party audit.

## What stashr provides today

**Inherited FIPS cryptography.** stashr performs no cryptography of its own beyond the .NET
BCL, which delegates to the operating system. When deployed on a FIPS-configured host —
**Red Hat Enterprise Linux with the OpenSSL FIPS provider**, or **Windows Server with CNG in
FIPS mode** — cryptographic operations use the OS vendor's **FIPS 140-2/140-3 validated
module**. stashr therefore *inherits* validated cryptography on those platforms; it is **not**
itself a CMVP-validated module, and we do not claim to be.

> Publishable stance: *"stashr uses 100% FIPS-validated cryptographic modules provided by the
> host operating system."* Not *"stashr is FIPS certified."*

**A built-in control architecture.** Isolation, sealed-by-default operation, Shamir
split-knowledge unseal, deny-by-default policy, tamper-evident hash-chained audit, mTLS +
AppRole machine auth, and OIDC + MFA for operators. See the
[Control Evidence Matrix](docs/compliance/control-evidence-matrix.md).

**Bring-your-own-compliance hooks.** OIDC SSO so you authenticate operators against your own
IdP/MFA (Keycloak, Entra ID); pluggable audit sinks that stream redacted JSONL to your own
certified SIEM/WORM storage. stashr provides the hooks; your certified infrastructure does the
heavy lifting.

## In progress / pursuing

- **OpenSSF Best Practices Badge** — a free self-certification of secure release management,
  version control, testing, and vulnerability handling. *Pursuing; not yet awarded.*
- **OWASP Top 10 attestation** — a transparent ledger documenting how the codebase mitigates
  the OWASP Top 10. *In progress.*
- **CI security evidence** — dependency scanning, SAST, and a per-release SBOM published as
  build artifacts.

## Not yet (on the roadmap, stated plainly)

- **Independent third-party security audit + penetration test.** The real trust unlock. Planned
  once the codebase is mature; the report will be published.
- **SOC 2 Type II / ISO 27001.** These assess our *organization*, require a company entity and
  a multi-month observation window, and are planned for the commercial phase.
- **FedRAMP.** Applies to cloud service offerings. stashr is self-hosted software; a deploying
  US agency authorizes *their* system that includes stashr. Not a near-term product milestone.

## How we keep these claims honest

- Self-certifications (OpenSSF, OWASP) are labeled as such and are **not** described as audits.
- "FIPS" is always qualified as *inherited from a FIPS-configured host*, never as our own
  certification.
- This page changes only when reality changes. If you find a claim here you believe is
  imprecise, please open an issue — we treat compliance-language accuracy as a bug class.

_Last reviewed: 2026-06-05._
