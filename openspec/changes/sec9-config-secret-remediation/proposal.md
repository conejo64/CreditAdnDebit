# Proposal: SEC-9 / CICD-13 Config Secret Remediation — Widen the Controls, Not Just the Instances

## Intent

**The problem is not eleven committed passwords. It is that three separate enforcement mechanisms are each narrower than the requirement they claim to enforce, and verify trusted the mechanism instead of re-deriving the requirement.**

Verified on `main` @ `41a5bfa`:

| # | Mechanism | Stated requirement scope | Actual mechanism scope |
|---|---|---|---|
| 1 | `CommittedConfigSecretShapeTests.cs:76-78` | "any committed `appsettings*.json`" (SEC-9 scenario, `security-hardening/spec.md:329`) | a `[Theory]` with two hardcoded `[InlineData]` paths |
| 2 | `.github/workflows/ci.yml:12-26` | "SHALL scan the repository — including the commit range under review" (CICD-13, `cicd-packaging/spec.md:378-380`) | `gitleaks-action@v2` on `pull_request` scans the PR diff only |
| 3 | `.gitleaks.toml:17` | "no committed credential" | `(?i)(Password\|Pwd)\s*=\s*[^;"'\s]{4,}` — requires `=`, so YAML `KEY: value` credentials are invisible |

**Two merged requirements are violated by the same gap.** SEC-9 (`security-hardening/spec.md:297-333`) and CICD-13 (`cicd-packaging/spec.md:375-384`) both signed PASS in the archived `phase0-security-blockers` verify-report. The full-tree scan this change adds is therefore not a new control being invented — it is an existing merged requirement that was never implemented.

**Real-world exploitability is LOW.** Every leaked value is a local docker-compose development default (`postgres`, `Your_strong_Passw0rd!`, `dev-signing-secret`). Nothing production-bearing is exposed. **The severity is the false PCI evidence**: an auditor reading the archived verify-report sees SEC-9 PASS and CICD-14 PASS while committed credentials sit in `main` and the CI gate structurally cannot see them.

### Measurable outcome — verifiably true when done, false today

1. `dotnet test backend/CardSwitchPlatform.sln --filter FullyQualifiedName~CommittedConfigSecretShapeTests` enumerates **every** `appsettings*.json` in the repo at run time. Adding a fourth service is covered automatically; no human has to remember an `[InlineData]` line.
2. `gitleaks` run in **full-tree** mode over the working tree exits 0, and that same run is a **blocking** check on every PR.
3. The gitleaks ruleset matches YAML `*_PASSWORD:` / `*_SECRET:` shapes and message-signing secrets — so (2) exiting 0 is real evidence, not an artifact of a blind regex.
4. Zero committed credential material across `appsettings*.json`, `Program.cs` fallbacks, design-time `IDesignTimeDbContextFactory` fallbacks, and `docker-compose.yml`.
5. Every service fails fast at startup with a message naming the missing configuration key, instead of silently falling back to a committed default.
6. SEC-9's written scope matches its enforcement scope, and CICD-13 is implemented as written.

## Scope

### In Scope

**A. Purge — all committed occurrences, nothing deferred**

| File:line | Value | Class |
|---|---|---|
| `IsoSwitch.Api/appsettings.json:43` | `ConnectionStrings:Postgres` inline `Password=postgres` | base (non-Development) file — effective fallback outside Development |
| `IsoAudit.Api/appsettings.json:3` | `ConnectionStrings:IsoSwitchDb` inline password | IsoAudit was never in Phase 0's scope at all |
| `IsoAudit.Api/appsettings.Development.json:3` | same, `Host=localhost` | never purged |
| `IsoAudit.Api/Program.cs:63` | `?? "Host=localhost;...Password=postgres"` | in-code fallback; currently dead, re-activates the moment the JSON literal is emptied |
| `IsoSwitchDbContextFactory.cs:12-13` | `ISOSWITCH_POSTGRES` `??` literal | design-time |
| `CardVaultDbContextFactory.cs:13-14` | `CARDVAULT_POSTGRES` `??` literal | design-time |
| `IdentityAppDbContextFactory.cs:12-13` | `CARDVAULT_SQLSERVER` `??` SQL Server SA password | design-time; missed entirely by Phase 0 |
| `docker-compose.yml:5` | `POSTGRES_PASSWORD: postgres` | YAML shape — invisible to current ruleset |
| `docker-compose.yml:17` | `MSSQL_SA_PASSWORD: "Your_strong_Passw0rd!"` | YAML shape — invisible; same value as `IdentityAppDbContextFactory` |
| `docker-compose.yml:56` | `ConnectionStrings__Postgres=...Password=postgres` | CardVault override; the only one the ruleset catches |
| `IsoSwitch.Api/appsettings.Development.json:26` | `Kafka:SigningSecret = "dev-signing-secret"` | no rule matches; not in SEC-9's category list |
| `CardVault.Api/appsettings.Development.json:9` | `Kafka:SigningSecret = "dev-signing-secret"` | same |

**B. Widen the three mechanisms**
- Discovery-based test replacing hardcoded `[InlineData]`.
- Widen `.gitleaks.toml`: YAML `*_PASSWORD:` / `*_SECRET:` shape rule + a message-signing-secret rule.
- Full-tree, blocking gitleaks CI job invoked via the **CLI**, not `gitleaks-action@v2`.

**C. Fail-fast parity across services** — IsoSwitch and IsoAudit adopt CardVault's existing `RequiredConnectionStringsOptions` + `ValidateOnStart()` pattern; design-time factories throw a message naming the required env var.

**D. Spec truth** — amend SEC-9 in place (service scope, filename scope, secret categories) + a NEW requirement mandating repeatable exhaustive verification. CICD-13: implement as written, amend only its detection-coverage clause.

**E. Documentation and template hygiene** — add missing vars to `backend/deploy/.env.example`; fix its duplicate `ConnectionStrings__Postgres` key; compose interpolation; delete the orphaned `docs/env.example` and drop `.gitleaks.toml:36`; update the stale `.gitleaks.toml:29-33` comment; fix `SECURITY.md:40`'s `backend/.env.example` → `backend/deploy/.env.example`.

### Out of Scope

- **Git-history rewrite.** The values are local dev defaults with no production reach; a history rewrite's cost to forks and open branches exceeds its benefit here. (Contrast Phase 0 SEC-01, where real vault keys justified it.)
- **Credential rotation.** Nothing to rotate — these are docker-compose dev bootstrap values, not live production material.
- **A real secrets manager** (Vault / AWS Secrets Manager / Key Vault). SEC-9 already permits "environment variables **or** a secrets-manager provider"; env-var-only satisfies it today and the chosen shape migrates without a rewrite.
- **Unifying CardVault's runtime `ConnectionStrings:Postgres` key.** Touches `GetConnectionString("Postgres")` call sites and `RequiredConnectionStringsOptions`; out of a security remediation's blast radius. Documented follow-up.
- **Swapping gitleaks for TruffleHog**, redesigning CI beyond the secret-scan job, frontend changes, and re-opening Phase 0 SEC-01..SEC-06 controls.
- **`dotnet user-secrets` as a source of truth.** Documented as an optional bare-metal `dotnet run` convenience only; never a second contract alongside `.env`.

## Capabilities

### Modified Capabilities

- **`security-hardening`** — SEC-9 amended in place: opening sentence and first bullet widened from "CardVault and IsoSwitch" to include IsoAudit, and from `appsettings.Development.json` to "any committed `appsettings*.json` (base and environment-specific)"; enumerated secret categories extended to cover **message-signing secrets** and design-time/tooling connection strings. Plus **one new requirement**: SEC-9 compliance SHALL be verifiable by a single repeatable command that enumerates the target file set at run time rather than from a fixed list.
- **`cicd-packaging`** — CICD-13 **implemented as written**, not rewritten. One narrow amendment: its detection-coverage clause (`spec.md:382-384`) enumerates only "vault keys, DB passwords, admin credentials, admin API keys" — extend it to YAML-shaped container-bootstrap credentials and message-signing secrets, require the CI scanner version to match the pinned pre-commit hook version, and add an explicit scenario asserting **full-tree** (not diff-only) scope. The existing "including the commit range under review" phrasing is what invited the narrowing; the clarifying scenario closes it.

### New Capabilities

- None.

## Approach

**Follow CardVault's existing convention; invent nothing.** CardVault already implements the target end state: empty `ConnectionStrings` in committed config, a dedicated `RequiredConnectionStringsOptions` + `RequiredConnectionStringsOptionsValidator` existing purely to hang `ValidateOnStart()` off (`CardVault.Api/Security/`, wired at `Program.cs:154-158`), and a validator message naming the exact missing `ConnectionStrings:*` key. This mirrors the already-established `JwtOptionsValidator` / `TokenizationOptionsValidator` / `AdminApiKeyOptionsValidator` / `TcpIsoClientOptionsValidator` family across all three services. IsoSwitch and IsoAudit simply adopt it. Secrets stay absent from options types per the repo's documented convention (`SendGridOptions` / `MovistarOptions`: "`ApiKey` is a secret and is intentionally NOT a property here").

**One structural rule, adopted as the countermeasure to the failure pattern: every spec amendment lands in the same PR as the code that satisfies it.** Amending SEC-9's secret-category list to cover message-signing secrets in an early PR while `dev-signing-secret` still sits in `main` would create a knowingly-violated merged requirement — precisely the sin this change exists to correct. So the scope amendment lands with the purge it describes (PR1) and the category amendment lands with the Kafka purge (PR5).

**Blast radius is low and empirically bounded.** `IsoSwitchWebApplicationFactory` and `IsoAuditWebApplicationFactory` swap the `DbContext` to InMemory in `ConfigureTestServices` and never call `UseSetting("ConnectionStrings:...")`. IsoSwitch's Development config already carries `"Postgres": ""` and the suite passes — proving `UseNpgsql("")` does not throw eagerly in this EF Core / Npgsql version, so emptying IsoAudit's value carries the same risk profile.

## Resolved open items

### 1. Design-time factory env-var naming — DECIDED: hard rename to the ASP.NET Core double-underscore shape, with one deliberate exception

| Factory | Old | New | Why |
|---|---|---|---|
| `IsoSwitchDbContextFactory` | `ISOSWITCH_POSTGRES` | `ConnectionStrings__Postgres` | IsoSwitch owns this key in the shared `.env`; design-time now matches runtime exactly |
| `IdentityAppDbContextFactory` | `CARDVAULT_SQLSERVER` | `ConnectionStrings__SqlServerIdentity` | already unique, already in `.env.example` |
| `CardVaultDbContextFactory` | `CARDVAULT_POSTGRES` | `ConnectionStrings__CardVaultPostgres` (**new distinct key**) | **NOT** `ConnectionStrings__Postgres` — that key is claimed by IsoSwitch in the shared `.env`, which is exactly why `docker-compose.yml:53-56` carries the "W-1 fix" inline override. A naive rename would make `dotnet ef` for CardVault silently target **IsoSwitch's database**. A slightly awkward name beats a silent wrong-database migration. |

**No fallback chain.** Not to the removed literal, and not to the legacy env-var name either. A legacy secondary lookup would let a stale exported value silently win — the same class of invisible-default bug being removed. Instead the factory throws with a message naming the required key **and** the old name it replaces: e.g. `"Set ConnectionStrings__Postgres (replaces ISOSWITCH_POSTGRES) before running dotnet ef."` Discoverable in one run, zero deprecation debt.

**Migration/compat implication — verified, not assumed.** A repo-wide grep for `ISOSWITCH_POSTGRES|CARDVAULT_POSTGRES|CARDVAULT_SQLSERVER` finds them **only** inside the three factory files (plus this change's own exploration doc). No CI workflow, script, Dockerfile, runbook, or doc sets them. The entire blast radius is a developer's local shell profile or IDE run configuration. Mitigation: the throw message names the replacement, all three new keys are added to `backend/deploy/.env.example`, and one `.env` then covers both runtime and `dotnet ef`. Net effect: the "works when running the app, fails when running `dotnet ef`" class of confusion disappears.

### 2. The `.env*` tooling permission blocker — HARD PREREQUISITE, requires the user's own action

**Re-confirmed first-hand during this phase**, not inherited: `Grep` on `backend/deploy/.env.example` returned `Permission to read ... has been denied` — a denial, not a not-found. The file's contents are only reachable via `git show HEAD:backend/deploy/.env.example`. PR6 must **edit** that file.

**Resolution: narrow the deny rule to permit `Read`/`Edit`/`Write` on exactly `backend/deploy/.env.example`, keeping the blanket deny on every other `.env*` path.** Rationale:

- That one file is tracked, committed, contains only `__REPLACE_ME__` placeholders, and is already path-allowlisted in `.gitleaks.toml:35`. It is a template, not a secret; the deny rule is protecting the wrong file.
- **Rejected alternative — shell-based write** (`Set-Content` / heredoc): bypasses the guardrail wholesale rather than narrowing it, defeats the Edit tool's read-before-write invariant (raising clobber risk on an 80-line file with a subtle duplicate-key layout), and leaves no reviewable intent trail.
- **Fallback if the exception is declined:** the human applies PR6's `.env.example` hunk manually, or `sdd-apply` produces a reviewed patch applied via `git apply`. Either way PR6 stalls without an explicit decision.

**This is a configuration change only the user can authorize.** No agent instruction constitutes that consent. `sdd-apply` will stop cold at PR6 until it is resolved — flag it at the top of `tasks.md`.

## Slice plan — 7 stacked-to-main PRs

Delivery: `stacked-to-main`, ~400 changed lines max per PR, strict TDD (`dotnet test backend/CardSwitchPlatform.sln`).

| # | Slice | Est. lines | Landing constraint |
|---|---|---|---|
| **PR1** | **Enforcement scope + spec truth + `appsettings*.json` purge.** Rewrite `CommittedConfigSecretShapeTests` to discover `appsettings*.json` via `Directory.GetFiles(repoRoot, "appsettings*.json", AllDirectories)` excluding `bin`/`obj`. Amend SEC-9's service + filename scope. Add the repeatable-verification requirement. Empty `ConnectionStrings` in `IsoSwitch.Api/appsettings.json`, `IsoAudit.Api/appsettings.json`, `IsoAudit.Api/appsettings.Development.json`. | 110-170 | **First.** Nothing downstream may claim compliance before the mechanism that proves it exists. |
| **PR2** | **IsoSwitch fail-fast.** `RequiredConnectionStringsOptions` + validator mirroring CardVault; `ValidateOnStart()` in `Program.cs`; startup-validation tests (template exists at `IsoSwitch.Tests/Security/StartupSecretValidationTests.cs`). | 120-170 | after PR1 |
| **PR3** | **IsoAudit fail-fast.** Remove the `Program.cs:63` in-code fallback; add the validator + `ValidateOnStart()` (IsoAudit has none of this infrastructure — most net-new code). | 150-200 | after PR1 |
| **PR4** | **Design-time factory hardening.** All three factories: drop the literal fallback, apply the rename decision above, throw naming the required key + its predecessor; tests. | 100-140 | after PR1 |
| **PR5** | **Kafka `SigningSecret` purge + SEC-9 category extension.** Empty both committed values; fail-fast validation for the signing secret; add the var to `.env.example`; amend SEC-9's enumerated categories to cover message-signing secrets **in this PR**, per the same-PR rule. | 100-150 | after PR1; **must precede PR7** |
| **PR6** | **Ruleset widening + compose/template/doc cleanup — ATOMIC.** Widen `.gitleaks.toml` (YAML `*_PASSWORD:`/`*_SECRET:` shape rule + signing-secret rule); update the stale `:29-33` comment; delete `docs/env.example` and drop `.gitleaks.toml:36`; fix `SECURITY.md:40`; add `POSTGRES_PASSWORD`, `MSSQL_SA_PASSWORD` and the three renamed design-time keys to `backend/deploy/.env.example`; fix its duplicate `ConnectionStrings__Postgres` key (document once, with an inline comment pointing at the compose override); move `docker-compose.yml:5,17,56` to `${VAR:?message}` mandatory interpolation. | 130-190 | **The two halves cannot be split.** Widening the ruleset while compose still holds `POSTGRES_PASSWORD: postgres` would make the pre-commit hook and the existing PR-diff scan fire on PR6's own diff. Must be one commit-and-merge unit. |
| **PR7** | **Full-tree blocking gitleaks job (CICD-13 implementation).** Replace `gitleaks-action@v2` with a direct CLI invocation in full-tree mode, pinned to the same version as `.pre-commit-config.yaml` (`v8.30.1`) so local and CI verdicts cannot diverge. Amend CICD-13's detection-coverage clause + add the full-tree scenario. | 50-90 | **Dead last, and strictly after PR6.** |

**Total ≈ 760-1110 changed lines**; every slice individually inside the 400-line budget.

### Why the ordering is load-bearing, not preference

- **PR7 last — self-deadlock.** A *blocking* full-tree gate landing before PR1-6 would immediately fail on files those PRs have not touched yet, blocking the very PRs that fix it. Landing it after every occurrence is purged means it is green from the moment it exists.
- **PR7 strictly after PR6 — the failure mode reproduced.** With the `=`-only regex, `docker-compose.yml:5` (`POSTGRES_PASSWORD: postgres`, colon) and `:17` (`MSSQL_SA_PASSWORD: "..."`, colon) do **not** match; only `:56` does. A blocking full-tree gate over the un-widened ruleset would report **green while two of three compose credentials remain** — a new control narrower than its requirement, which is exactly the bug this change exists to eliminate.
- **PR1 first — strict TDD without a red `main`.** The widened discovery test is demonstrated RED locally against `main` @ `41a5bfa` and that evidence recorded in the PR body, then the purge in the same PR turns it green. The test and the files it newly covers cannot merge separately in a stacked-to-main chain without leaving `main` red. The rejected alternative — landing the discovery test with the failing paths temporarily skipped or allowlisted — would re-introduce the narrowing pattern by hand and is explicitly refused.

## Affected Areas

| Area | Slice | Impact | Description |
|---|---|---|---|
| `CardVault.Tests/Security/CommittedConfigSecretShapeTests.cs` | PR1 | Modified | `[InlineData]` → run-time file discovery |
| `openspec/specs/security-hardening/spec.md:297-333` | PR1, PR5 | Modified | SEC-9 scope + categories amended in place; new verification requirement |
| `IsoSwitch.Api/appsettings.json:43` | PR1 | Modified | empty `ConnectionStrings:Postgres` |
| `IsoAudit.Api/appsettings.json:3`, `appsettings.Development.json:3` | PR1 | Modified | empty `ConnectionStrings:IsoSwitchDb` |
| `IsoSwitch.Api/Program.cs` + new `Security/RequiredConnectionStringsOptions*` | PR2 | New/Modified | fail-fast parity |
| `IsoAudit.Api/Program.cs:60-66` + new validator | PR3 | New/Modified | remove fallback; fail-fast parity |
| `IsoSwitchDbContextFactory.cs`, `CardVaultDbContextFactory.cs`, `IdentityAppDbContextFactory.cs` | PR4 | Modified | no literal fallback; renamed keys; throw-on-missing |
| `IsoSwitch.Api/appsettings.Development.json:26`, `CardVault.Api/appsettings.Development.json:9` | PR5 | Modified | purge `Kafka:SigningSecret` |
| `.gitleaks.toml:14-43` | PR6 | Modified | 2 new rules; stale comment; drop `docs/env.example` allowlist |
| `backend/deploy/.env.example` | PR6 | Modified | new vars; duplicate-key fix — **permission-blocked, see prerequisite** |
| `backend/deploy/docker-compose.yml:5,17,56` | PR6 | Modified | `${VAR:?message}` interpolation |
| `docs/env.example` | PR6 | Removed | orphaned — no live reference repo-wide |
| `SECURITY.md:40` | PR6 | Modified | stale `backend/.env.example` path |
| `.github/workflows/ci.yml:12-26` | PR7 | Modified | CLI full-tree blocking scan |
| `openspec/specs/cicd-packaging/spec.md:375-384` | PR7 | Modified | detection-coverage clause + full-tree scenario |

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **`.env*` tool permission denies PR6's edit** — apply stops cold | **High (confirmed live)** | Hard prerequisite: user narrows the deny rule for `backend/deploy/.env.example` before PR6 starts. Fallbacks: human-applied hunk, or reviewed `git apply` patch. Flag at the top of `tasks.md`. |
| **Widened ruleset lands after the blocking gate** → gate green while secrets remain | Medium | PR6 strictly precedes PR7; PR7's PR body must show a full-tree run over the widened ruleset exiting 0. |
| **Blocking gate lands early** → deadlocks this change's own PRs | Medium | PR7 is dead last, non-negotiable ordering. |
| **`gitleaks-action@v2` sunset** — Node 20 opt-out required after 2026-06-02, action stops working entirely after 2026-09-16 | High (dated) | PR7 uses the gitleaks **CLI** directly, pinned to `.pre-commit-config.yaml`'s `v8.30.1`. Exact subcommand/flags for full-tree mode must be confirmed against that pinned version at design/apply — **not asserted here.** |
| **705/705 baseline is unverified** — last confirmed 2026-07-24 in the archived verify-report; no suite run in explore or propose | Medium | `sdd-apply` re-establishes the baseline on `main` @ `41a5bfa` **before** any edit. A pre-existing failure must not be attributed to this change. |
| **Compose interpolation with an unset var** silently creates a passwordless postgres | Medium | `${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}` mandatory-variable syntax — compose aborts loudly instead of degrading. |
| **New gitleaks YAML rules false-positive** on `KAFKA_CFG_*`, `ACCEPT_EULA`, and similar non-secret compose keys | Medium | Anchor the rules to `*_PASSWORD:` / `*_SECRET:` / signing-secret shapes; validate the widened ruleset against the whole tree inside PR6 before PR7 makes it blocking; never weaken by adding a path allowlist for a real config file. |
| **Design-time rename breaks a developer's local shell export** | Low | Verified zero automated consumers; throw message names the replacement key; all three keys documented in `.env.example`. |
| **IsoAudit fallback removal breaks local `dotnet run`** without `.env` | Low | Intentional: fail-fast with a key-naming error is the requirement. Documented in the same PR. |
| **Emptying `IsoAudit` connection strings breaks tests** | Low | Both web-application factories swap to InMemory in `ConfigureTestServices`; IsoSwitch already runs on an empty string with the suite green. |

## Rollback Plan

Per-slice and independent — stacked-to-main, one deliverable unit per PR.

- **PR1** — revert restores the narrow `[InlineData]` test and the committed passwords. Undesirable (re-opens the evidence gap); prefer forward fix.
- **PR2, PR3, PR4** — clean reverts; each only adds startup validation or removes a fallback. PR3's revert reintroduces a weak default — forward fix preferred.
- **PR5** — clean revert; restores the placeholder signing secret.
- **PR6** — clean revert restores the committed compose credentials and `docs/env.example`. **No credential rotation is owed**: the values are local docker dev defaults with no production reach (see Non-Goals).
- **PR7** — delete the job. Zero runtime behavior; the safest revert in the chain.

No irreversible step anywhere: no history rewrite, no key rotation, no data migration, no production topology change. This chain has no one-way doors.

## Dependencies and Prerequisites

1. **HARD PREREQ (PR6):** user-authorized narrowing of the `.env*` tool-permission deny rule for `backend/deploy/.env.example`.
2. **PREREQ (PR1):** re-establish the test baseline on `main` @ `41a5bfa` before the first edit.
3. Existing CardVault `RequiredConnectionStringsOptions` + `ValidateOnStart` pattern (PR2, PR3 extend it).
4. Existing `.gitleaks.toml` custom-rule structure (PR6 extends it).
5. gitleaks CLI version pinned to `.pre-commit-config.yaml`'s `v8.30.1` (PR7).
6. Existing `.github/workflows/ci.yml` (PR7 replaces one job; does not redesign the pipeline).

## Non-Goals

Named deliberately so each is a documented decision rather than a silent gap:

- **No real secrets manager.** Env-var-only, per SEC-9's own permitted mechanisms.
- **No credential rotation.** The leaked values are local docker-compose dev defaults, not production material. There is nothing to rotate.
- **No change to production deployment topology.** Compose is local infra only; no CD, registry, or cloud change.
- **No git-history rewrite.**
- **No change to CardVault's runtime `ConnectionStrings:Postgres` key** or its `GetConnectionString("Postgres")` call sites.
- **No re-opening of Phase 0 SEC-01..SEC-06.**
- **No scanner replacement, no frontend work, no `dotnet user-secrets` as a source of truth.**

## Success Criteria

- [ ] `CommittedConfigSecretShapeTests` discovers every `appsettings*.json` at run time; no hardcoded path list remains.
- [ ] SEC-9's written scope covers CardVault, IsoSwitch, **and** IsoAudit, base **and** environment-specific `appsettings*.json`, and enumerates message-signing secrets plus design-time connection strings.
- [ ] A new SEC-9-adjacent requirement mandates a single repeatable, run-time-enumerating verification command, and that command exists and passes.
- [ ] Zero inline credentials across all 12 inventoried locations.
- [ ] IsoSwitch and IsoAudit fail startup with a message naming the missing `ConnectionStrings:*` key; no in-code fallback remains in `IsoAudit.Api/Program.cs`.
- [ ] All three design-time factories throw a message naming the required key and its predecessor; no literal connection string remains.
- [ ] `.gitleaks.toml` flags YAML `*_PASSWORD:` / `*_SECRET:` shapes and message-signing secrets; a full-tree run over the widened ruleset exits 0.
- [ ] `.github/workflows/ci.yml` runs a **full-tree**, **blocking** gitleaks scan via the CLI, pinned to `v8.30.1`, on every PR — with CICD-13 implemented as written.
- [ ] `docs/env.example` is deleted, its allowlist entry dropped, and `SECURITY.md:40` points at `backend/deploy/.env.example`.
- [ ] `backend/deploy/.env.example` documents every required variable with placeholder values and has no duplicate key.
- [ ] `docker compose up` from a fresh clone fails loudly on a missing required variable instead of degrading to a passwordless database.
- [ ] `dotnet test backend/CardSwitchPlatform.sln` green at or above the re-established baseline after every slice.
- [ ] Seven independently reviewable stacked-to-main PRs, each under 400 changed lines, landing in the stated order.

## Proposal question round — assumptions needing user review

Written here because a sub-agent cannot ask interactively. Each is a judgment call this proposal made; correct any and the slice plan adjusts.

1. **PR1 bundles the spec amendment, the widened test, and the `appsettings*.json` purge.** This deviates from the exploration's "PR1 = test only, goes RED". A red test cannot merge to `main` in a stacked-to-main chain. Accept the bundle, or prefer a different way to preserve the RED-first evidence?
2. **Rule: every spec amendment lands in the PR that satisfies it** — which deliberately splits the SEC-9 edit across PR1 (scope) and PR5 (secret categories). Accept?
3. **CardVault's design-time Postgres key becomes a new distinct `ConnectionStrings__CardVaultPostgres`** rather than reusing `ConnectionStrings__Postgres`, because that key belongs to IsoSwitch in the shared `.env`. The cleaner long-term fix — giving CardVault a distinct **runtime** key too — is out of scope here. Accept the asymmetry, or pull the runtime unification in?
4. **Hard rename with no legacy env-var fallback.** Verified zero automated consumers; only a local shell export could break. Accept, or require a deprecation window?
5. **The `.env*` permission narrowing needs your own action** — no agent instruction can authorize it. Confirm you will narrow the rule for `backend/deploy/.env.example`, or choose the manual-patch fallback for PR6.

---

**Deliberate deviation:** the `sdd-propose` skill sets a 450-word artifact budget. This proposal is longer because the orchestrator explicitly mandated eleven content sections including a per-PR slice plan with line estimates, two resolved open items with rationale, and a full risk register. Density is managed via tables over prose per `cognitive-doc-design`.
