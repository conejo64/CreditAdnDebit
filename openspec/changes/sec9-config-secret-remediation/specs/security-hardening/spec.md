# Delta Spec — SEC-9 / CICD-13 Config Secret Remediation
# Capability: security-hardening
# Change: sec9-config-secret-remediation
# Base spec: openspec/specs/security-hardening/spec.md (SEC-9 at lines 297-333; SEC-1..SEC-12 in force)

This document records ONLY what changes. It describes the WHAT (behavioral contracts), not the HOW.

This change adds no new capability. It **amends an already-merged main requirement whose written scope is
narrower than its own intent**, and adds one requirement that makes SEC-9 mechanically verifiable. Unchanged
behavior from the base spec is not repeated.

---

## Amendment Notes

- **SEC-9 is amended in place, not superseded.** Its behavioral intent ("no live secret in any committed file,
  env-only loading at runtime") is unchanged. What changes is the *written* scope: the requirement's own
  narrow bullet contradicted its own broad scenario, and the narrow bullet is what got implemented.
- **All four base SEC-9 scenarios are PRESERVED.** None is dropped. Two are reproduced with one mechanical
  correction, stated here rather than made silently: their `GIVEN` said "the repository at the tip of the
  phase0 branch". That branch no longer exists, so the scenario is not verifiable as written. It becomes "the
  repository at the tip of `main`". No assertion changes.
- **The base spec's one-way-door note on git-history rewrite still stands and is not re-litigated here.** This
  change explicitly performs no history rewrite and no credential rotation; the leaked values are local
  development bootstrap defaults with no production reach.
- **Scope placement to note for review:** the committed-orchestration-file contract below lands on SEC-9
  rather than on `cicd-packaging` CICD-12, because the committed compose credentials are part of SEC-9's "no
  committed file SHALL contain live secret material" clause and the change authorizes amendments to exactly
  two requirements.
- **Cross-capability dependency — SEC-13 depends on CICD-13.** The second half of SEC-13's verification below
  *is* the full-tree secret scan required by `cicd-packaging` CICD-13. If CICD-13 ever regresses to scanning
  only the commit range under review, SEC-13 silently loses its coverage of every purge class that is not an
  `appsettings*.json` — source-file fallbacks, design-time entry points, container-orchestration files, and
  templates. This coupling is stated in the amendment notes of **both** delta files deliberately: the gap
  between two requirements that each assumed the other covered something is exactly how the condition this
  change remediates arose.

---

## MODIFIED Requirements

### Requirement SEC-9: No Secret Material in Committed Configuration — Env-Only Secret Loading (modified — widened service, file, secret-category, and template scope)

CardVault, IsoSwitch, **and IsoAudit** SHALL load all secret material — vault encryption keys, database
connection-string passwords, JWT signing keys, tokenization secrets, seed administrative credentials, admin
API keys, **message-signing secrets**, and **design-time / tooling connection strings** — exclusively from
environment variables or a secrets-manager configuration provider. No committed file in the repository SHALL
contain live secret material.

Specifically:

- **Any committed `appsettings*.json` — base *and* environment-specific — for CardVault, IsoSwitch, and
  IsoAudit** SHALL NOT contain any live vault key (`Vault:Keys:*`), any connection string with an inline
  password, any seed credential, any admin API key, or any message-signing secret value. A non-Development
  file is in scope on the same terms as a Development file: a committed base value is the effective fallback
  in every environment where the override is absent.
- No committed source file SHALL supply a credential-bearing fallback in code (for example a null-coalescing
  default that yields a connection string containing a password). Absent configuration SHALL produce a
  fail-fast error, never a working weak default.
- Design-time / tooling entry points (for example an `IDesignTimeDbContextFactory` used by EF Core CLI
  commands) SHALL read their connection string from the environment only, and SHALL fail with an error naming
  the required configuration key when it is absent. They SHALL NOT carry a literal connection string, and
  SHALL NOT silently fall back to a differently-named legacy variable.
- Secret-bearing options types SHALL follow the established repo convention: the secret property is
  intentionally absent from the options type and read from configuration/environment directly (as with
  `SendGridOptions` / `MovistarOptions`, "`ApiKey` is a secret and is intentionally NOT a property here").
- Committed container-orchestration files SHALL reference every credential as a **mandatory** external
  variable. When such a variable is unset, the orchestration command SHALL abort with an error naming the
  variable, and SHALL NOT start a container with a default, empty, or absent credential.
- The repository SHALL provide **exactly one** canonical committed non-secret environment template. It SHALL
  document every variable an operator must supply, declare each variable name **at most once**, and use values
  that are obviously non-secret. Every committed document or tool configuration that references an environment
  template SHALL resolve to that one canonical path; no second, independently-editable template SHALL exist.

> **One-way-door note (git history rewrite):** unchanged from the base spec. The *behavioral* contract this
> spec pins is "no live secret in any committed file and env-only loading at runtime". History-scrub and
> key-rotation sequencing remain owned by SEC-01/vault-and-pci, and are explicitly not performed by this
> change.

#### Scenario: Committed development config contains no live vault key

*(preserved from base spec; `GIVEN` retargeted from the retired phase0 branch to `main`)*

- GIVEN the repository at the tip of `main`
- WHEN `appsettings.Development.json` for CardVault is inspected
- THEN it contains no value matching a Base64 AES-256 key under `Vault:Keys`
- AND the previously committed values `k1` and `k2` are absent

#### Scenario: Committed config contains no inline connection-string password

*(preserved from base spec; `GIVEN` retargeted to `main`)*

- GIVEN the repository at the tip of `main`
- WHEN any committed `appsettings*.json` is inspected
- THEN no connection string contains an inline `Password=` value
- AND the connection strings are sourced at runtime from environment variables (e.g. `ConnectionStrings__Postgres`)

#### Scenario: Base (non-Development) and IsoAudit config are in scope on the same terms

- GIVEN the repository at the tip of `main`
- WHEN the base `appsettings.json` of CardVault, IsoSwitch, and IsoAudit is inspected
- AND the `appsettings.Development.json` of IsoAudit is inspected
- THEN no `ConnectionStrings:*` value in any of them contains an inline `Password=` value
- AND every `ConnectionStrings:*` key that the service requires is present but empty, so the operator-supplied
  environment variable is the only source of the value

#### Scenario: No committed source file carries a credential-bearing code fallback

- GIVEN the repository at the tip of `main`
- WHEN committed source files are inspected for a connection-string literal containing a password
- THEN no such literal is present in any application startup path
- AND no configuration read is followed by a fallback that yields a credential-bearing default

#### Scenario: Design-time tooling connection string is environment-only and names its required key

- GIVEN the environment variable that supplies a service's design-time connection string is unset
- WHEN an EF Core design-time command is executed for that service
- THEN the command fails before opening any database connection
- AND the error message names the exact required environment variable
- AND the error message names the legacy variable it replaces, if the variable was renamed
- AND no connection is attempted against any literal fallback value, and no differently-named legacy variable
  is consulted

#### Scenario: No committed message-signing secret value

- GIVEN the repository at the tip of `main`
- WHEN every committed configuration file is inspected for a message-signing secret key (e.g. `Kafka:SigningSecret`)
- THEN every such key is either absent or set to an empty value
- AND the previously committed value `dev-signing-secret` is absent from all committed files

#### Scenario: Missing required secret env var causes fail-fast startup

*(preserved from base spec, unchanged)*

- GIVEN a required secret variable (e.g. `Vault__Keys__<activeKeyId>` or `ConnectionStrings__Postgres`) is
  absent from all configuration sources
- WHEN `CardVault.Api` host starts
- THEN the host throws before accepting any HTTP traffic
- AND the process exits with a non-zero exit code
- AND the error message references the missing configuration key so the operator knows what to supply

#### Scenario: Every service fails fast on a missing connection string instead of using a committed default

- GIVEN a service is one of CardVault, IsoSwitch, or IsoAudit
- AND the `ConnectionStrings:*` value that service requires is absent or empty in every configuration source
- WHEN that service's host starts
- THEN the host fails before accepting any HTTP traffic and the process exits with a non-zero exit code
- AND the error message names the specific missing `ConnectionStrings:*` key
- AND the service does not start against any committed default value

#### Scenario: Container orchestration aborts loudly on an unset required credential variable

- GIVEN a required credential variable consumed by the committed container-orchestration definition is unset
- WHEN the orchestration bring-up command is executed from a fresh clone
- THEN the command aborts with a non-zero exit code before any container starts
- AND the error identifies the unset variable by name
- AND no container is created with an empty, absent, or default credential

#### Scenario: A committed environment template documents required variables with non-secret placeholders

*(preserved from base spec; extended with the single-canonical-template and no-duplicate-key assertions)*

- GIVEN the committed environment templates in the repository
- WHEN they are enumerated and inspected
- THEN exactly one canonical template exists
- AND it lists every operator-supplied variable name, including container-bootstrap credentials and
  design-time / tooling connection strings
- AND every value is an obvious placeholder (empty or a clearly non-secret token), never live secret material
- AND no variable name is declared more than once within it
- AND every committed document or tool configuration that references an environment template resolves to that
  canonical path

---

## ADDED Requirements

### Requirement SEC-13: SEC-9 Compliance Is Verifiable by a Repeatable, Run-Time-Enumerating Verification Whose Coverage Equals SEC-9's Purge Surface

SEC-9 compliance SHALL be verifiable by a repeatable verification whose target set is **enumerated from the
repository at run time**, and whose coverage SHALL equal **the entire file surface SEC-9 constrains** — not a
subset of it.

That surface comprises: any committed `appsettings*.json` (base and environment-specific), committed source
files that could carry a credential-bearing fallback, design-time / tooling entry points, committed
container-orchestration files, and committed environment templates.

The verification is the **union of two commands**:

- **(a) A committed-configuration shape verification** that discovers every committed `appsettings*.json` in
  the repository at run time, excluding build output directories, and asserts SEC-9's positive structural
  constraints on each: that every `ConnectionStrings:*` key the service requires is present but empty, that no
  value contains an inline password, that seed-credential and admin-key values are empty, and that specific
  previously leaked values are absent.
- **(b) A full-tree secret scan** over the committed working tree, which covers every remaining class in the
  surface above without requiring a per-class discovery predicate.

Both commands SHALL satisfy all of the following:

- Neither SHALL contain an enumerated literal list of file paths as its target set. A file added in the future
  SHALL be covered without editing either command.
- Each SHALL report the set of files it inspected, so coverage is observable rather than assumed.
- Each SHALL exit with a non-zero code if and only if it detects a violation within its own scope.
- The union SHALL be satisfiable only when **both** commands are present and pass. Neither command alone
  SHALL be treated as establishing SEC-9 compliance.

> **Why both halves are required, and why neither subsumes the other.** They assert different kinds of thing.
> (b) can only detect the *presence of a secret pattern*; it structurally cannot assert that a required
> `ConnectionStrings:*` key is present but empty, nor that one specific historical value is absent — absence
> of a matched pattern is not presence of the required shape. Conversely (a) cannot be extended to cover
> source-file fallbacks, design-time entry points, or orchestration files, because there is no honest discovery
> predicate for "every source file that might contain a weak credential fallback". The resolution is not to
> enumerate file classes but to scan the whole tree. Dropping either half leaves a real, demonstrable gap.

> **Why this requirement exists at all.** SEC-9 was reported PASS while committed credentials sat in `main`.
> The single root cause was that the test operationalizing SEC-9's "any committed `appsettings*.json`" scenario
> was a parameterized test with two hardcoded paths. It passed, and a verifier reasonably read "tests green" as
> evidence. A verification whose scope is narrower than the requirement it claims to enforce produces false
> evidence. So exhaustiveness must be structural, and the *comparison between what was checked and what the
> requirement names* must be possible without a human re-deriving it — the omission of exactly that comparison
> is the documented cause of the false PASS.

#### Scenario: A newly added config file is covered without editing the verification

- GIVEN the SEC-9 verification passes on the repository
- AND a new committed `appsettings*.json` containing an inline `Password=` value is added at a path that
  appears nowhere inside either command's own source
- WHEN the verification is run again with no edit whatsoever to either command
- THEN it exits with a non-zero code
- AND its output names the newly added file's path as the violating file

#### Scenario: Reported coverage equals the discovered file set

- GIVEN the repository contains a set S of committed `appsettings*.json` files outside build output directories
- WHEN command (a) runs
- THEN the set of files it reports as inspected is exactly S
- AND no member of S is skipped, filtered out, or excluded by an allowlist

#### Scenario: Neither command carries a fixed target path list

- GIVEN the source of command (a) and the configuration of command (b)
- WHEN each is inspected
- THEN each defines its targets by run-time discovery over the repository
- AND neither contains an enumerated literal list of file paths serving as its target set

#### Scenario: Command (a) alone does not satisfy SEC-13 — a credential outside `appsettings*.json` is still caught

- GIVEN a credential-bearing connection-string literal is planted in a design-time tooling entry point
- AND a second credential is planted in the committed container-orchestration file
- WHEN command (a) is run alone
- THEN it exits with code 0, because neither file is an `appsettings*.json`
- AND SEC-13 is NOT satisfied by that result
- WHEN the union of (a) and (b) is run
- THEN it exits with a non-zero code
- AND both planted credentials are reported, each identified by file

#### Scenario: Command (b) alone does not satisfy SEC-13 — a missing required key is still caught

- GIVEN a required `ConnectionStrings:*` key is removed entirely from a committed `appsettings*.json`, or set
  to a non-empty value that matches no secret pattern
- WHEN command (b) is run alone
- THEN it exits with code 0, because no secret pattern is present in the tree
- AND SEC-13 is NOT satisfied by that result
- WHEN the union of (a) and (b) is run
- THEN it exits with a non-zero code
- AND the violation is reported naming the affected file and the affected `ConnectionStrings:*` key

#### Scenario: Union coverage is stated explicitly and comparable to SEC-9's named surface

- GIVEN a verifier assessing SEC-9 compliance
- WHEN the union of (a) and (b) is run
- THEN the run reports which file classes were inspected and, for command (a), which individual files
- AND that report can be compared against the file classes SEC-9 names without inspecting any file by hand and
  without re-deriving the target set from the requirement text
- AND any SEC-9-named class absent from the report is identifiable as a coverage gap from the report alone

#### Scenario: Exit code reflects violation state exactly

- GIVEN a repository in which the whole SEC-9 surface satisfies SEC-9
- WHEN the union of (a) and (b) runs
- THEN it exits with code 0
- AND GIVEN the same repository with a single credential reintroduced anywhere in that surface
- WHEN the union runs again
- THEN it exits with a non-zero code and names the offending file
