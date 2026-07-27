# Delta Spec — Phase 0 Security Blockers
# Capability: cicd-packaging
# Change: phase0-security-blockers
# Base spec: openspec/specs/cicd-packaging/spec.md (ola1-cicd-dockerfiles CICD-1..CICD-12)

This document records ONLY what changes. It describes the WHAT (build/pipeline contracts), not the HOW.
It ADDS a secret-scanning gate (SEC-06) to the existing `.github/workflows/ci.yml` and a matching local
pre-commit hook. It does NOT redesign the pipeline; the base CICD-1 three-job structure is unchanged, this
adds one gate job and its local counterpart. The base spec's invariant CICD-INV-5 (build-only, no push) is
unaffected.

---

## ADDED Requirements

### Requirement CICD-13: Secret-Scanning CI Job That Fails on Detection

`.github/workflows/ci.yml` SHALL include a secret-scanning job (using gitleaks or TruffleHog) that runs on
every push and pull-request to `main` (consistent with the base CICD-1 trigger). The job SHALL scan the
repository — including the commit range under review — and SHALL exit with a non-zero code, failing the
workflow run, when a secret is detected. When no secret is detected, the job SHALL exit with code 0.

The job MAY use a tuned allowlist / baseline so that the committed `.env.example` obvious placeholders and
other known non-secret values do not produce false positives, but the allowlist SHALL NOT suppress detection
of the previously leaked classes of secret (vault keys, DB passwords, admin credentials, admin API keys).

#### Scenario: Pushing a commit containing a secret fails the pipeline

- GIVEN a commit introduces a value matching a secret pattern (e.g. a Base64 AES-256 key or an inline DB password)
- WHEN the secret-scanning job runs in CI
- THEN the job exits with a non-zero code
- AND the workflow run is marked as failed

#### Scenario: A clean commit passes the secret-scanning job

- GIVEN a commit introduces no secret material
- AND any placeholder values are covered by the tuned allowlist / baseline
- WHEN the secret-scanning job runs
- THEN the job exits with code 0
- AND the workflow run is not failed by this job

#### Scenario: Placeholder values in .env.example do not trigger a false positive

- GIVEN `.env.example` contains obvious non-secret placeholder values
- WHEN the secret-scanning job runs
- THEN the placeholders are not reported as secrets
- AND the job does not fail solely because of the `.env.example` placeholders

---

### Requirement CICD-14: Pre-Commit Secret-Scanning Hook

The repository SHALL provide a committed pre-commit hook configuration (e.g. `.pre-commit-config.yaml` or an
equivalent committed git-hook definition) that runs the same class of secret scanner locally and blocks a
commit when a secret is detected in the staged changes, so a secret is caught before it enters history. The
hook SHALL be reproducible from committed configuration (an operator can install it without ad-hoc setup).

#### Scenario: Staged secret blocks the commit locally

- GIVEN the pre-commit hook is installed from the committed configuration
- AND a staged change contains a value matching a secret pattern
- WHEN the developer attempts to commit
- THEN the pre-commit hook exits non-zero
- AND the commit is aborted before the secret enters history

#### Scenario: Clean staged changes commit normally

- GIVEN the pre-commit hook is installed
- AND the staged changes contain no secret material
- WHEN the developer commits
- THEN the hook passes and the commit proceeds

#### Scenario: Pre-commit configuration is committed and reproducible

- GIVEN a fresh clone of the repository
- WHEN an operator installs the pre-commit hooks from the committed configuration
- THEN the secret-scanning hook is registered without additional ad-hoc setup

---

## Invariants (SHALL NOT Change)

- The base CICD-1 three-job structure (`build-test`, `build-frontend`, `docker-build`) is preserved; the
  secret-scanning job is additive.
- CICD-INV-5 (build-only, no image pushed to any registry) remains true.
- No application source code behavior is changed by this delta; it is pipeline/tooling only.

---

## Out-of-Scope Confirmations (Phase 0)

- CD pipeline, registry push, or cloud deployment — owned by the broader `cicd-packaging` roadmap, not here.
- Redesign of the existing CI jobs beyond adding the secret-scanning gate.
