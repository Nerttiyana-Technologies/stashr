# Security Policy

stashr is a secrets-management product. We take security reports seriously and appreciate
responsible disclosure.

## Pre-audit status

stashr `0.9.x` is a **pre-audit** release: it has not yet undergone an independent third-party
security assessment. Use it for evaluation, internal tooling, and non-critical workloads. Do not
store crown-jewel production secrets in stashr until the independent audit completes. See
[COMPLIANCE.md](COMPLIANCE.md) for the full, precise posture.

## Supported versions

During the pre-1.0 phase, only the latest `0.9.x` release receives security fixes.

| Version | Supported |
|---------|-----------|
| 0.9.x   | ✅        |
| < 0.9   | ❌        |

## Reporting a vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Instead, report privately via one of:

- GitHub's **private vulnerability reporting** ("Report a vulnerability" under the repository's
  Security tab), or
- email **security@nerttiyana.com** with the details.

Please include:

- a description of the issue and its impact,
- steps to reproduce (a proof of concept if possible),
- affected version/commit, and
- any suggested remediation.

### What to expect

- We aim to acknowledge a report within **3 business days**.
- We'll work with you on a fix and a coordinated disclosure timeline, and credit you in the
  release notes unless you prefer to remain anonymous.
- Please give us a reasonable window to remediate before any public disclosure.

## Scope

In scope: the stashr engine, server, SDK, CLI, web UI, and configuration providers in this
repository. Out of scope: issues that require a pre-existing compromise of the host OS, the
PostgreSQL backend, or the operator's root token; and the inherent pre-audit caveats stated above.

Thank you for helping keep stashr and its users safe.
