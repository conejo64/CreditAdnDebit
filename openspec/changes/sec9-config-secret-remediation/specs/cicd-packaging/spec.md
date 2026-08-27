# Delta Spec — SEC-9 / CICD-13 Config Secret Remediation
# Capability: cicd-packaging
# Change: sec9-config-secret-remediation
# Base spec: openspec/specs/cicd-packaging/spec.md (CICD-13 at lines 375-406; CICD-1..CICD-14 in force)

This document records ONLY what changes. It describes the WHAT (pipeline/gate contracts), not the HOW.

This change adds no new requirement to this capability and does not redesign the pipeline. CICD-13 is
**implemented as written** — the requirement was already correct and the implementation was narrower than it.
This delta narrowly amends CICD-13's detection-coverage clause, adds a scanner-version-parity clause, and adds
scenarios that make the already-required scope unambiguous. The base CICD-1 job structure and the CICD-INV-5
build-only invariant are unaffected.

---

## Amendment Notes

- **CICD-13's first paragraph is reproduced VERBATIM and is deliberately not rewritten.** It already requires
  the job to "scan the repository — including the commit range under review". The requirement was not the
  defect; the implementation was. Rewriting it would obscure that the gate was owed all along.
- **What actually failed is that "including the commit range under review" was read as "only the commit range
  under review".** That ambiguity is closed by an explicit scenario below rather than by editing the sentence,
  so the audit trail shows the requirement standing and the implementation being brought up to it.
- **All three base CICD-13 scenarios are PRESERVED**, including the one guaranteeing that committed template
  placeholder values do not produce a false positive.
- CICD-14 (the local pre-commit hook) is otherwise unchanged. It is referenced below only as the source of the
  pinned scanner version that CI must match.
- **Cross-capability dependency — `security-hardening` SEC-13 depends on this requirement.** SEC-13 defines
  SEC-9's verification as the union of a discovery-based committed-configuration test **and this job's
  full-tree scan**. This job is therefore the only mechanism covering SEC-9's non-`appsettings*.json` purge
  classes: credential-bearing fallbacks in committed source files, design-time / tooling entry points,
  committed container-orchestration files, and committed environment templates. **Narrowing this job's scope
  back to the commit range under review would silently break SEC-13 without failing anything in this
  capability.** The coupling is stated in the amendment notes of both delta files deliberately: the gap between
  two requirements that each assumed the other covered something is exactly how the condition this change
  remediates arose.

---

## MODIFIED Requirements

### Requirement CICD-13: Secret-Scanning CI Job That Fails on Detection (modified — detection coverage, scanner-version parity, explicit full-tree scope)

`.github/workflows/ci.yml` SHALL include a secret-scanning job (using gitleaks or TruffleHog) that runs on
every push and pull-request to `main` (consistent with the base CICD-1 trigger). The job SHALL scan the
repository — including the commit range under review — and SHALL exit with a non-zero code, failing the
workflow run, when a secret is detected. When no secret is detected, the job SHALL exit with code 0.

> **Scope clarification (this amendment).** "Scan the repository" means the **full committed working tree at
> the checked-out revision**. Restricting the scan to the files changed in the commit range under review does
> NOT satisfy this requirement: a credential in a file the change does not touch is exactly the case this gate
> exists to catch. The commit range is included in the scan because it is part of the tree, not as the scan's
> boundary. See the full-tree scenario below.

The job MAY use a tuned allowlist / baseline so that the committed environment-template placeholder values and
other known non-secret values do not produce false positives. The allowlist SHALL NOT suppress detection of the
previously leaked classes of secret, which for this repository comprise:

- vault encryption keys,
- database connection-string passwords, in any delimiter form,
- **YAML-shaped container-bootstrap credentials**, that is a credential-bearing key mapped to a value by a
  colon rather than by an equals sign (for example a `*_PASSWORD:` mapping in a container-orchestration file),
- **message-signing secrets**,
- seed administrative credentials,
- admin API keys.

Furthermore:

- Detection SHALL NOT depend on any single delimiter. A credential SHALL be detected whether the value is
  bound by `=` or by `:`.
- The allowlist SHALL NOT be extended to cover a real (non-template) service configuration file. Suppressing a
  live configuration file by path is prohibited; the remedy for a finding in a live configuration file is to
  remove the credential, not to exempt the file.
- Every path pattern in the allowlist SHALL resolve to at least one committed file, so an allowlist entry
  cannot silently outlive the file it was written for.
- The scanner version the CI job executes SHALL be **exactly the version pinned in the committed pre-commit
  hook configuration** (CICD-14), so a local pre-commit verdict and a CI verdict over the same working tree
  cannot diverge.
- The job SHALL be built on a tool invocation that does not depend on a deprecated or sunset CI action, so the
  gate does not silently stop enforcing.

#### Scenario: Pushing a commit containing a secret fails the pipeline

*(preserved from base spec, unchanged)*

- GIVEN a commit introduces a value matching a secret pattern (e.g. a Base64 AES-256 key or an inline DB password)
- WHEN the secret-scanning job runs in CI
- THEN the job exits with a non-zero code
- AND the workflow run is marked as failed

#### Scenario: A clean commit passes the secret-scanning job

*(preserved from base spec, unchanged)*

- GIVEN a commit introduces no secret material
- AND any placeholder values are covered by the tuned allowlist / baseline
- WHEN the secret-scanning job runs
- THEN the job exits with code 0
- AND the workflow run is not failed by this job

#### Scenario: Placeholder values in the committed environment template do not trigger a false positive

*(preserved from base spec, unchanged in substance)*

- GIVEN the committed environment template contains obvious non-secret placeholder values
- WHEN the secret-scanning job runs
- THEN the placeholders are not reported as secrets
- AND the job does not fail solely because of those placeholders

#### Scenario: A credential in a file the pull request does not touch is still detected (full-tree scope)

- GIVEN a pull request to `main` whose diff touches only `README.md`
- AND a committed file that the pull request does not touch contains an inline database password
- WHEN the secret-scanning job runs for that pull request
- THEN the job reports that credential
- AND the job exits with a non-zero code
- AND the workflow run is marked as failed, so the pull request's checks are not green

> This scenario is the one a diff-scoped implementation cannot pass and a full-tree implementation passes
> unconditionally. It is the discriminating case; a job that is green here is not implementing CICD-13.

#### Scenario: A colon-delimited YAML credential is detected without an equals sign

- GIVEN a committed YAML file maps a credential-bearing key to a value using a colon, with no equals sign
  present — for example a `*_PASSWORD:` entry in a container-orchestration file, both unquoted and quoted
- WHEN the secret-scanning job runs
- THEN each such value is reported
- AND the job exits with a non-zero code
- AND detection does not require an `=` delimiter anywhere in the matched text

#### Scenario: A committed message-signing secret is detected

- GIVEN a committed configuration file sets a message-signing secret key (e.g. `Kafka:SigningSecret`) to a
  non-empty value
- WHEN the secret-scanning job runs
- THEN that value is reported
- AND the job exits with a non-zero code

#### Scenario: Non-secret configuration keys do not false-positive

- GIVEN committed container-orchestration files contain non-secret configuration entries whose keys resemble
  credential keys or whose values are opaque tokens — for example broker configuration keys, a licence
  acceptance flag, and a database user name
- WHEN the secret-scanning job runs against a tree containing no real credential
- THEN none of those entries is reported
- AND the job exits with code 0
- AND no live configuration file was path-allowlisted in order to achieve this

#### Scenario: CI scanner version matches the pinned pre-commit hook version

- GIVEN the committed pre-commit hook configuration pins the secret scanner at version V
- WHEN the CI secret-scanning job runs
- THEN it executes the same scanner at exactly version V
- AND a local hook run and the CI run over the same working tree and the same ruleset produce the same verdict
  and the same finding set

#### Scenario: Every allowlisted path resolves to a committed file

- GIVEN the secret-scanning configuration's path allowlist
- WHEN each allowlisted path pattern is resolved against the committed tree
- THEN every pattern matches at least one committed file
- AND no allowlisted pattern matches a live service configuration file
