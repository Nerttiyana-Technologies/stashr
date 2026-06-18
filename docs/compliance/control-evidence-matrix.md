# Control Evidence Matrix

A living map from regulatory controls to the stashr mechanism that addresses them, the design
reference, and the current implementation status. Its purpose is twofold: give enterprise
evaluators a clear picture, and serve as the **evidence pack** that makes a future third-party
audit faster and cheaper (an auditor that receives this does far less discovery).

**Status legend:** `Designed` = decided in an ADR, not yet built · `Implemented` = code exists
· `Verified` = covered by tests/CI evidence or external review.

> Scope note: stashr **provides controls**; certification is earned by the deploying
> organization. See [/COMPLIANCE.md](../../COMPLIANCE.md). FIPS is **inherited** from a
> FIPS-configured host, not a stashr CMVP validation.

## Cryptography & key management

| Control(s) | Requirement (paraphrased) | stashr mechanism | Ref | Status |
|------------|---------------------------|------------------|-----|--------|
| NIST SC-13; PCI 3; HIPAA 164.312(a)(2)(iv) | Use validated/approved cryptography | OS-provided **FIPS-validated** modules via `OsCryptoProvider`; approved algorithms only; **fail-closed** if not FIPS in regulated profile; startup KAT | ADR-0004 | Designed |
| NIST SC-12; PCI 3.5–3.6 | Key establishment, storage, rotation | Envelope hierarchy master→KEK→DEK; versioned keys; rewrap-based rotation | ADR-0004, ADR-0007 | Designed |
| NIST SC-28; PCI 3.4 | Protect data at rest | AES-256-GCM ciphertext + wrapped keys in store; master key never persisted | ADR-0004, ADR-0007 | Designed |
| NIST SC-12 (dual control); PCI 3.6.6 | Split knowledge & dual control of keys | **Shamir quorum** unseal — no single operator can unseal | ADR-0002, ADR-0012 | Designed |
| — | Protect secret material in memory | No `System.String` for secrets; pinned/unmanaged buffers; `ZeroMemory`; `mlock`/`VirtualLock`; no core dumps | ADR-0015 | Designed |

## Access control & authentication

| Control(s) | Requirement | stashr mechanism | Ref | Status |
|------------|-------------|------------------|-----|--------|
| NIST AC-3, AC-6; PCI 7 | Least privilege, need-to-know, deny-by-default | Path-capability policies, deny-by-default, per-app scoping; **explain-access** tool | ADR-0008 | Designed |
| NIST IA-2; PCI 8 | Identify & authenticate; MFA | mTLS + AppRole (machines); OIDC + **enforced MFA** (operators) | ADR-0001, ADR-0006 | Designed |
| NIST IA-5 | Authenticator management | Wrapped, single-use `secret_id` delivery; secret_id TTL/num_uses/CIDR bindings | ADR-0006, ADR-0011 | Designed |
| NIST AC-12 | Session termination | Short-lived tokens/leases; renew within max_ttl; revoke by token/accessor/lease/prefix | ADR-0006 | Designed |

## Audit & accountability

| Control(s) | Requirement | stashr mechanism | Ref | Status |
|------------|-------------|------------------|-----|--------|
| NIST AU-2, AU-3, AU-12; PCI 10; HIPAA 164.312(b) | Record who/what/when/where, allowed or denied | Append-only JSONL audit of request+response; value/token referenced by HMAC, never plaintext | ADR-0005 | Designed |
| NIST AU-9; PCI 10.5 | Protect audit information from tampering | Keyed **HMAC hash-chain**; genesis bound to keyVersion/unseal-session; high-water-mark + off-box signed checkpoints (truncation defense); `sys/audit/verify` | ADR-0005 | Designed |
| NIST AU-6 | Audit review / integration | Pluggable sinks → external SIEM; verifiable WORM export | ADR-0005 | Designed |

## System & communications protection

| Control(s) | Requirement | stashr mechanism | Ref | Status |
|------------|-------------|------------------|-----|--------|
| NIST SC-7, SC-8 | Boundary & transmission protection | mTLS on all engine endpoints; fencing + fail-closed HA | ADR-0002 | Designed |
| NIST SC-39 | Process isolation | Isolated, hardened engine process; master key never shares host memory | ADR-0001 | Designed |
| NIST CP / availability | Resilience without serving bad data | Active/standby HA; fail-closed under partition (self-seal + fencing) | ADR-0002 | Designed |

## Secure SDLC / organizational (SOC 2 CC, NIST SA/CM)

| Control(s) | Requirement | stashr mechanism | Ref | Status |
|------------|-------------|------------------|-----|--------|
| SOC 2 CC8; NIST CM-3, SA-11 | Change management, secure development | SAST + dependency + secret scanning in CI; **NetArchTest** boundary gates; analyzer banning raw crypto calls | ADR-0014 | Designed |
| SOC 2 CC7 | Supply-chain integrity | Reproducible + signed builds; per-release **SBOM** | ADR-0014 | Designed |
| — | Independent assurance | Third-party security audit + pentest (planned); OpenSSF Best Practices Badge + OWASP attestation (pursuing) | road-to-9.5 §8 | Planned |

---

_Most rows are `Designed` because stashr is pre-implementation: the architecture is decided in
the ADRs but code has not yet been written. Status will move to `Implemented`/`Verified` as
modules land with tests and CI evidence. This honesty is deliberate — see
[/COMPLIANCE.md](../../COMPLIANCE.md)._
