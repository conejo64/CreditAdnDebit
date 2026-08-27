# Exploration: sec9-config-secret-remediation

> Corrected v2 — see the changelog at the end of this document for what was withdrawn from v1 and why.
> Mirrored in Engram under topic key `sdd/sec9-config-secret-remediation/explore`.

## Current State

SEC-9 (`openspec/specs/security-hardening/spec.md:297-333`, merged main spec from the archived `phase0-security-blockers` change) requires env-only secret loading and asserts committed config has no inline connection-string passwords. This is FALSE on current `main`. Full inventory below, existing config-loading conventions, breakage analysis, CI/test gap analysis, the spec contradiction, and the single root cause of the broken evidence chain.

**Verification note:** the exploration sub-agent had no shell/git tool. All file-content claims below were confirmed via direct `Read`/`Grep` on the literal file path (not `Glob`, which does not match dotfiles by default and caused a v1 error on `.env.example`). One path (`backend/deploy/.env.example`) is blocked by an active permission rule that denies `Read`/`Grep` on `.env*` paths outright ("Permission ... has been denied", not a not-found error) — its existence and content are taken from orchestrator `git ls-files` / `git show HEAD:` verification.

## 1. Exact Secret Inventory (file:line, classification)

**Inline connection-string passwords (gitleaks custom rule `inline-connection-string-password` matches all of these) — directly read and confirmed:**

| File:Line | Value | Classification |
|---|---|---|
| `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json:43` | `ConnectionStrings:Postgres = "Host=postgres;...Password=postgres"` | REAL RISK — this is the BASE (non-Development) file. Since `appsettings.Development.json` overrides `Postgres` to `""`, this committed value is the effective fallback in any non-Development environment (e.g. a misconfigured Production/staging deploy) where the env var isn't set. Also outside SEC-9's literal bullet scope (bullet only names `appsettings.Development.json`), so it is not covered by scenario 1 wording but IS covered by scenario 2 ("any committed `appsettings*.json`"). |
| `backend/services/IsoAudit/src/IsoAudit.Api/appsettings.json:3` | `ConnectionStrings:IsoSwitchDb = "Host=postgres;...Password=postgres"` | REAL RISK — IsoAudit was never named in SEC-9 or the Phase 0 proposal scope at all (`proposal.md:32,167` name only "CardVault and IsoSwitch"). Total scope gap. |
| `backend/services/IsoAudit/src/IsoAudit.Api/appsettings.Development.json:3` | same value, `Host=localhost` | REAL RISK — unlike CardVault/IsoSwitch, IsoAudit's Development file was never purged. |
| `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs:63` | `?? "Host=localhost;...Password=postgres"` | REAL RISK — in-code silent fallback used when config resolves null. Config currently never resolves null since `appsettings.json` always supplies the leaked default, so this fallback is presently dead code — but it re-activates as a hidden weak default the moment the `appsettings.json` literal is emptied, unless removed together. |
| `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/IsoSwitchDbContextFactory.cs:13` | `Environment.GetEnvironmentVariable("ISOSWITCH_POSTGRES") ?? "Host=localhost;...Password=postgres"` | Design-time-only fallback — local-dev default, low exploitability (only runs under the `dotnet ef` CLI), but matches the gitleaks rule and violates SEC-9's literal "no committed file SHALL contain live secret material". |
| `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/CardVaultDbContextFactory.cs:14` | `Environment.GetEnvironmentVariable("CARDVAULT_POSTGRES") ?? "Host=localhost;...Password=postgres"` | Same as above — NOTE: exists even though CardVault's runtime `appsettings.json`/`appsettings.Development.json` are otherwise SEC-9-compliant (empty). The design-time factory was missed by Phase 0 entirely. |
| `backend/services/CardVault/src/CardVault.Infrastructure.Identity/Auth/IdentityAppDbContextFactory.cs:13` | `Environment.GetEnvironmentVariable("CARDVAULT_SQLSERVER") ?? "Server=localhost,11433;...Password=Your_strong_Passw0rd!;..."` | NEW FINDING (not in the original known-file list) — confirmed by orchestrator. Same pattern, SQL Server SA password, also missed by Phase 0. |
| `backend/deploy/docker-compose.yml:56` | `ConnectionStrings__Postgres=Host=postgres;...Password=postgres` (env var value, `cardvault` service override) | REAL RISK — inline in the compose `environment:` block instead of sourced from `.env`/interpolation. Exists because `.env.example`'s `ConnectionStrings__Postgres` is already claimed by IsoSwitch (see Section 4) — the "W-1 fix" comment in `.env.example` documents this constraint explicitly. |
| `backend/deploy/docker-compose.yml:5` | `POSTGRES_PASSWORD: postgres` (postgres container bootstrap) | Local-dev default, low external exploitability (container-internal), but a plaintext committed credential; in scope per user decision (ALL committed occurrences, nothing deferred) — move to `.env`. |
| `backend/deploy/docker-compose.yml:17` | `MSSQL_SA_PASSWORD: "Your_strong_Passw0rd!"` (sqlserver container bootstrap) | Same class as above; matches the `IdentityAppDbContextFactory.cs` fallback value 1:1 (same password reused in two places). In scope — move to `.env`. |

**Not matched by any current gitleaks rule but adjacent to SEC-9's intent:**

- `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json:26` and `backend/services/CardVault/src/CardVault.Api/appsettings.Development.json:9` — `Kafka:SigningSecret = "dev-signing-secret"`. Low-entropy, obviously fake value; the regex `(?i)(Password|Pwd)\s*=` and the vault-base64 rule do not match this JSON key/value shape, so gitleaks does not flag it today. SEC-9's named secret categories (vault keys, connection-string passwords, JWT signing keys, tokenization secrets, seed admin credentials, admin API keys) do NOT explicitly list "Kafka message-signing secret".
  **DECIDED (user, settled): IN SCOPE.** Purge the value AND extend SEC-9's enumerated secret categories to include message-signing secrets, plus add a gitleaks rule that detects this shape. Rationale: a signing secret authenticates messages; closing only the value while leaving the category uncontrolled would reproduce this change's own root cause (a control narrower than the requirement it claims to enforce). This overrides the exploration's initial recommendation to treat it as a documented exclusion.

**Confirmed NOT a violation (already resolved by prior SEC-01/SEC-05/SEC-11 work, verified by reading current file contents):**

- `Vault:Keys` — empty `{}` in CardVault's Development config (previously leaked `k1`/`k2` values are gone; `CommittedConfigSecretShapeTests.cs` pins this with exact leaked-value regression checks).
- `Jwt:SigningKey` / `Jwt:Key` — empty string in all three services' committed files, protected by `ValidateOnStart` (CardVault `JwtOptionsValidator`, IsoSwitch has its own, IsoAudit via `JwtOptionsValidator` + ADR-1/ADR-2 in `Program.cs:21-49`).
- `Tokenization:Secret` — empty, `ValidateOnStart` via `TokenizationOptionsValidator`.
- `Admin:ApiKey` (IsoSwitch) — empty, `ValidateOnStart` via `AdminApiKeyOptionsValidator`, rejects the literal `"dev-admin-key"`.
- `Seed:AdminEmail` / `AdminPassword` / `OpenBankingClientSecret` (CardVault) — empty (SEC-05 already removed the default admin seed).
- False positive: `frontend/src/app/features/auth/login.component.ts` — contains the string `password` only as an HTML form-control type/label, not a credential.
- Already reviewed/allowlisted: `CardVault.Tests/Migrations/AddPinKdfColumnsMigrationTests.cs:26` (dummy Npgsql string for SQL generation only, no real connection — see `.gitleaksignore:6-9`).

## 2. Existing Configuration Conventions (follow, do not invent)

CardVault already implements the target end state and should be the template:

- `appsettings.json` / `appsettings.Development.json` carry a `ConnectionStrings` section that is present but **empty** (`"Postgres": ""`, `"SqlServerIdentity": ""`).
- A dedicated fail-fast options type exists purely to hang `ValidateOnStart()` off of: `backend/services/CardVault/src/CardVault.Api/Security/RequiredConnectionStringsOptions.cs` + `RequiredConnectionStringsOptionsValidator.cs`, wired in `Program.cs:154-158`. The validator returns a clear `ValidateOptionsResult.Fail` naming the exact missing `ConnectionStrings:*` key. This mirrors the already-established `JwtOptionsValidator` / `TokenizationOptionsValidator` / `AdminApiKeyOptionsValidator` / `TcpIsoClientOptionsValidator` pattern used across all three services.
- The actual `DbContext` registration still reads the raw string via `builder.Configuration.GetConnectionString("Postgres")` (`Program.cs:169`) — the options type above never touches the real connection, it exists solely for startup validation.
- Secret-bearing options intentionally omit the secret property from the options class. SEC-9's spec text explicitly cites this for `SendGridOptions`/`MovistarOptions`: "`ApiKey` is a secret and is intentionally NOT a property here".
- Tests supply connection strings via `builder.UseSetting("ConnectionStrings:Postgres", "...")` in `CardVaultWebApplicationFactory.cs:56-60`, then immediately swap the `DbContext` to InMemory — the real Npgsql/SqlServer string is a syntactic placeholder only, never opened.

**IsoSwitch and IsoAudit have NOT adopted this pattern.** IsoSwitch's `Program.cs:103-107` registers `AddDbContext<IsoSwitchDbContext>` reading `GetConnectionString("Postgres")` with no fallback and **no `RequiredConnectionStringsOptions` equivalent** — meaning IsoSwitch already runs today with an empty Postgres string in Development (proven empirically: `appsettings.Development.json` already has `"Postgres": ""` and the 705-test baseline passes) but with no fail-fast. A real misconfiguration would surface as a raw `NpgsqlException`/`ArgumentException` at first DB access, not a clear startup error. IsoAudit is worse: its `Program.cs:60-66` still carries an in-code weak-default fallback and no validator at all.

## 3. What Breaks / Blast-Radius Analysis

- **Tests: LOW risk.** `IsoSwitchWebApplicationFactory.cs` and `IsoAuditWebApplicationFactory.cs` never call `UseSetting("ConnectionStrings:...")` — they replace the `DbContext` with `UseInMemoryDatabase` in `ConfigureTestServices`, which runs after the real `AddDbContext` registration but before any actual DB access. IsoSwitch's Development config is *already* an empty Postgres string and 705/705 passes today, which empirically proves `UseNpgsql("")` does not throw eagerly in this Npgsql/EF Core version — so emptying IsoAudit's committed value carries the same low risk.
- **`dotnet ef` design-time tooling: MEDIUM.** All three `IDesignTimeDbContextFactory` classes read a custom, non-ASP.NET-Core-style env var (`ISOSWITCH_POSTGRES`, `CARDVAULT_POSTGRES`, `CARDVAULT_SQLSERVER` — single underscore, ALL CAPS) that is **different from** the double-underscore ASP.NET Core convention (`ConnectionStrings__Postgres`) used everywhere else, including in `.env.example`. Removing the inline fallback without reconciling this naming split means a developer who only populates `.env` from `.env.example` still cannot run `dotnet ef migrations add`. See Section 9.2.
- **`docker-compose.yml` fresh-clone flow: NORMAL, NOT broken.** *(Corrected from v1.)* `backend/deploy/.env.example` exists and is tracked in git (`git ls-files` confirms), is comprehensive (see Section 4), and is exactly the file the compose comment at line 39 references. `.env` itself is absent from the working tree because it is gitignored by design — copying `.env.example` → `.env` is the standard, documented onboarding step, not a broken flow. The v1 claim that this was "already broken independent of this change" was wrong and is withdrawn; it rested on a `Glob` miss for a dotfile pattern, not a real absence.
- **Key-naming inconsistency across services:** IsoSwitch binds `ConnectionStrings:Postgres`; IsoAudit binds `ConnectionStrings:IsoSwitchDb` — different key names for the same physical database. `.env.example` already documents both names separately, which is correct, but see Section 4 for a latent duplicate-key bug within the file itself.

## 4. Canonical `.env.example` — location resolved, one latent bug found

*(Corrected from v1 — the file exists, is comprehensive, and is canonical.)*

`backend/deploy/.env.example` is the canonical, tracked, ~80-line template file. It is the exact path named by `tasks.md:84,89-93`, `verify-report.md:45`, and `.gitleaks.toml:35` (`paths = ['''backend/deploy/\.env\.example''', ...]`) — three independent planning/tooling artifacts agree on this path, and it exists. `design.md:23,50`'s reference to `backend/.env.example` (no `deploy/` segment) is a stale doc typo in an already-archived design document, not evidence of drift. There is no real three-way path ambiguity.

Per `git show HEAD:` verification, the file documents (all values `__REPLACE_ME__`): `ConnectionStrings__Postgres`, `ConnectionStrings__IsoSwitchDb`, `ConnectionStrings__SqlServerIdentity`, `Vault__Keys__k3` (plus commented k1/k2 transition entries), `Vault__ActiveKeyId`, `Seed__AdminEmail`/`AdminPassword`/`OpenBankingClientSecret`, `Jwt__SigningKey`, `Jwt__Key` (with an explicit CICD-INV-6 note on the IsoAudit `Jwt:Key` vs `Jwt:SigningKey` asymmetry), `Jwt__Issuer`/`Jwt__Audience`, `Tokenization__Secret`, `Admin__ApiKey`, and `Cors__AllowedOrigins__0` — i.e. it already anticipates essentially everything this change needs, which substantially shrinks PR5's scope (see Section 10).

**What is genuinely still missing from `.env.example`** (net new for this change): container-bootstrap credentials (`POSTGRES_PASSWORD`, `MSSQL_SA_PASSWORD`) and the three design-time-factory env vars (`ISOSWITCH_POSTGRES`, `CARDVAULT_POSTGRES`, `CARDVAULT_SQLSERVER`, pending the naming reconciliation in Section 9.2).

**Latent bug found — duplicate key.** `ConnectionStrings__Postgres=__REPLACE_ME__` is declared TWICE in `.env.example` — once under the CardVault section, once under the IsoSwitch section. In dotenv/`env_file` semantics the last declaration wins when the file is loaded into a single process environment, so if a single shared `.env` were loaded verbatim by both services the second declaration would silently shadow the first. Today this is a latent trap rather than an active bug **only because** `docker-compose.yml:56` gives CardVault its own inline `environment:` override (`ConnectionStrings__Postgres=Host=postgres;...Database=cardvault;...`) that takes precedence over `env_file` for that one service — the file's own "W-1 fix" comment documents this exact constraint ("a single shared `.env` cannot hold two different Postgres databases").

The duplicate key is still a config trap for two reasons: (1) it misleads a reader — the file literally cannot set two different values for the same key, so the CardVault-section occurrence is dead documentation rather than an honored setting; (2) if the docker-compose inline override were ever removed or refactored without noticing this dependency, CardVault would silently start pointing at IsoSwitch's database.

Options:

- **(a)** Document `ConnectionStrings__Postgres` once, with an inline comment pointing at the docker-compose override for CardVault — lowest effort, keeps the existing override mechanism.
- **(b)** Give CardVault a genuinely distinct env var name (e.g. a per-service prefix) and update both `.env.example` and the compose override to match — cleaner long-term but touches the "one shared double-underscore ASP.NET Core key per connection string" assumption baked into `RequiredConnectionStringsOptions` / `GetConnectionString("Postgres")` call sites.

**Recommend (a)** — a documentation-only fix that removes the trap without touching the already-working, tested override mechanism, consistent with "smallest safe change" for a security-hardening remediation.

## 5. Current CI / Secret-Scan Setup

`.github/workflows/ci.yml:12-26` — the `secret-scan` job runs `gitleaks/gitleaks-action@v2` with `fetch-depth: 0`, on both `push` (main) and `pull_request` (to main). Per gitleaks-action v2 docs, on `pull_request` events the action scopes its scan to the PR's `BASE_SHA..HEAD_SHA` diff (`fetch-depth: 0` only enables diff computation; it does not trigger a full-tree scan). This confirms the premise: **files untouched by a PR are never scanned**, which is exactly why these long-committed passwords never tripped CI. There is no scheduled (`on: schedule`) or full-tree job anywhere in the workflow file.

Additionally (finding via web search): **gitleaks-action@v2 is being deprecated.** Node 20 actions including this one require `ACTIONS_ALLOW_USE_UNSECURE_NODE_VERSION=true` after 2026-06-02, and the action **stops working entirely after 2026-09-16**, regardless of opt-out. Per the user's decision (full-tree scan, BLOCKING on every PR), the new job should NOT be built on `gitleaks-action@v2` as-is; invoke the `gitleaks` CLI directly (maintained Docker image or binary) so it is not tied to the action's sunset date.

**DECIDED (user, settled): the full-tree job is BLOCKING on every PR, not merely scheduled.** This makes the landing sequence load-bearing, not a nicety. If this job landed before PR1-5 remediate every occurrence in Section 1, it would immediately go red and block ALL subsequent PRs in this very change — a self-inflicted deadlock, since the fix PRs themselves could not merge past a scan flagging files they haven't touched yet. Landing it dead last (PR6, Section 10), after every real-risk occurrence is remediated, means it goes green from the moment it exists.

`.pre-commit-config.yaml` (CICD-14, referenced in the archived verify-report as PASS) only registers a local `gitleaks` pre-commit hook (`rev: v8.30.1`). This hook is **opt-in per developer machine** (requires `pre-commit install`) and is **never run inside CI** — no CI step executes `pre-commit run --all-files`. So CICD-14's PASS only proves the hook config file exists, not that it ever executed against these files; it is a second layer that could theoretically have caught the leak, but only for a developer who installed it before committing.

**Stale comment:** `.gitleaks.toml:29-33` reads "Real config files (appsettings.Development.json for CardVault/IsoSwitch) are intentionally NOT listed here: the currently leaked Vault:Keys (k1/k2), connection-string passwords, and seed credentials in those files MUST stay detectable until SEC-01 purges them." This is doubly stale: (a) for CardVault/IsoSwitch `appsettings.Development.json` specifically, connection-string passwords and seed credentials already ARE purged (empty), so "MUST stay detectable" describes an already-resolved past state as if pending; (b) it never mentions the files that ARE currently leaking (IsoSwitch base `appsettings.json`, both IsoAudit files, `docker-compose.yml`, the three `DbContextFactory` classes) — so a reader trusting this comment would incorrectly conclude the leak surface is fully covered by the two named Development files. Needs an update as part of remediation (folded into PR5).

## 6. SEC-9 Text Analysis (verbatim + contradiction)

> **Requirement SEC-9: No Secret Material in Committed Configuration — Env-Only Secret Loading**
> CardVault and IsoSwitch SHALL load all secret material — vault encryption keys, database connection-string passwords, JWT signing keys, tokenization secrets, seed administrative credentials, and admin API keys — exclusively from environment variables or a secrets-manager configuration provider. No committed file in the repository SHALL contain live secret material.
>
> Specifically:
> - `appsettings.Development.json` (CardVault and IsoSwitch) SHALL NOT contain any live vault key (`Vault:Keys:*`), any connection string with an inline password, any seed credential, or any admin API key.
> - ...
>
> #### Scenario: Committed config contains no inline connection-string password
> - GIVEN the repository at the tip of the phase0 branch
> - WHEN any committed `appsettings*.json` is inspected
> - THEN no connection string contains an inline `Password=` value

**The contradiction, precisely:** the requirement's opening sentence and the second bullet scope the requirement to "CardVault and IsoSwitch", and the first bullet narrows the *tested file* to `appsettings.Development.json` only — but the general clause ("No committed file... SHALL contain live secret material") and the connection-string scenario ("any committed `appsettings*.json`") are both unscoped by service and by filename. IsoAudit and the base (non-Development) `appsettings.json` files fall exactly in that gap: named nowhere in the narrow bullet, but squarely inside the broad general clause and the broad scenario text.

**DECIDED (user, settled): Option A — amend SEC-9 in place**, rewriting the opening sentence and bullet to say "CardVault, IsoSwitch, and IsoAudit" and "any committed `appsettings*.json` (base and environment-specific)", and extending the enumerated secret categories to include message-signing secrets (per the Kafka decision in Section 1). "True breadth" was always the intent — the scenario text already says "any committed appsettings*.json"; the bullet was an incomplete transcription. This is a spec amendment to a MERGED main spec, not a delta on an active change — `sdd-spec` must handle it as an explicit "amend base spec" edit, not the normal ADDED/MODIFIED delta flow.

**Also DECIDED (user, settled): add a new requirement mandating a repeatable verification command.** This directly addresses Section 7's root cause: SEC-9 verification must not depend on a human remembering to keep a hardcoded test-file list in sync with the requirement's literal scope. The new requirement should mandate that SEC-9 compliance be checkable via a single repeatable command/test that enumerates the actual file set at run time, so a future `sdd-verify` pass has a mechanical, un-gameable way to confirm PASS rather than trusting a fixed list.

## 7. The Broken Evidence Chain — Root Cause (single cause)

*(Corrected from v1: the earlier claim that the verify-report's cited `.env.example` evidence file didn't exist was WRONG — it exists and is comprehensive. That evidence in `verify-report.md:45` was VALID and the claim is withdrawn entirely. This sharpens the root-cause story to a single precise cause.)*

The archived `verify-report.md:45` reports SEC-9 as **PASS**, citing: "CardVault.Api/appsettings.Development.json - Vault.Keys empty, Seed.* empty, ConnectionStrings.* empty; IsoSwitch.Api/appsettings.Development.json - Admin.ApiKey empty; backend/deploy/.env.example documents required vars with placeholders." Every clause of that citation is true and verifiable — the PASS was not based on a fabricated or missing artifact.

**The single root cause: the verifier trusted a test suite whose scope silently excludes the real leak.** `backend/services/CardVault/tests/CardVault.Tests/Security/CommittedConfigSecretShapeTests.cs` — the one test method that operationalizes the "no inline connection-string password" scenario — is a `[Theory]` at line 76 with **exactly two hardcoded `[InlineData]` entries** at lines 77-78, covering only CardVault's and IsoSwitch's `appsettings.Development.json`. It does not use file discovery/globbing against "any committed `appsettings*.json`" as the spec scenario literally requires. Consequently:

- IsoSwitch's *base* `appsettings.json` (the actual leak) is never checked by this test.
- IsoAudit is never checked at all — no `InlineData` entry exists for it.
- The verifier saw "705/705 tests passed" (including this file) and reasonably inferred SEC-9 was enforced, but the test's own scope was narrower than the requirement it claims to satisfy, and the verifier did not independently re-derive the requirement's literal scenario wording against a fresh grep of the repo. Everything else the verifier cited was correct and real — the test's scope, not the verifier's other evidence, is where the gap lives.

**This is a repeatable process gap, not a one-off cleanup, and it is exactly what the new "repeatable verification command" requirement (Section 6) is meant to close.** The fix should make `CommittedConfigSecretShapeTests` (or an equivalent) **discover** target files via `Directory.GetFiles(repoRoot, "appsettings*.json", SearchOption.AllDirectories)` (excluding `bin`/`obj`) rather than hardcoded `InlineData`, so a future new service or new config file is automatically covered and a verify pass can trust "tests green" as real evidence again. This is the single highest-leverage fix in this change: it converts a scenario that currently requires a human to remember to add an `InlineData` line into one that is structurally exhaustive.

## 8. Remediation Approach — DECIDED

**Mechanism: env vars + `.env.example` + docker-compose interpolation.** This is not a green-field choice — it is already the established, working convention (CardVault's `RequiredConnectionStringsOptions` + empty appsettings + docker-compose `env_file`, plus the already-comprehensive `backend/deploy/.env.example`), it is what SEC-9's own text names ("environment variables or a secrets-manager configuration provider"), and it is the only option that generalizes cleanly to a future real secrets manager without a rewrite. `dotnet user-secrets` is documented as an optional convenience for bare-metal `dotnet run` only — never a second source of truth alongside `.env`.

**Scope: ALL committed occurrences from Section 1** — IsoSwitch, all three IsoAudit locations, all three design-time factories (including the `IdentityAppDbContextFactory` finding), docker-compose, and the Kafka `SigningSecret` (with SEC-9 category extension + a new gitleaks rule). Nothing deferred.

## 9. Still Open for `sdd-propose`

1. **Design-time factory env-var naming reconciliation.** `ISOSWITCH_POSTGRES` / `CARDVAULT_POSTGRES` / `CARDVAULT_SQLSERVER` (single underscore, ALL CAPS, factory-only) vs. the `ConnectionStrings__*` double-underscore convention used everywhere else including `.env.example`. **Recommendation:** rename the factory-read env vars to reuse the standard `ConnectionStrings__Postgres` / `ConnectionStrings__SqlServerIdentity` names — `Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")` works identically to the ASP.NET Core convention since these are just process env vars. A single `.env` populated from the single `.env.example` would then cover both runtime and design-time tooling, removing an entire class of "works when running the app, fails when running `dotnet ef`" confusion. Needs explicit design-phase sign-off since it changes a currently-working (if awkwardly named) mechanism.
2. **`docs/env.example`'s fate: delete, or reduce to a pointer at the canonical `backend/deploy/.env.example`.** **Recommendation:** reduce to a one-line pointer (or delete and update onboarding docs to reference the canonical file directly) rather than maintaining two independently editable templates — two sources of truth for the same env-var contract is exactly the drift this change exists to eliminate.
3. **`.env*` tooling permission blocker (operational).** An active permission rule denies `Read`/`Grep`/`Write` on `.env*` paths for both the orchestrator and sub-agents (confirmed via explicit permission-denied errors, not not-found). `backend/deploy/.env.example` could only be inspected via `git show HEAD:<path>`. PR5 must edit that file, so the propose/design phase needs to decide the mechanism: adjust the permission rule for this path, or perform the edit through a shell-based write. This is a real execution blocker, not a theoretical one.

## 10. Slice Suggestion (stacked-to-main, ~400-line review budget per PR)

1. **PR1 — Prove the gap (spec + test-scope fix), ~80-150 lines.** Amend SEC-9 in place (Section 6, Option A) to close the CardVault/IsoSwitch-only and Development-only wording gap and extend the secret categories; add the new "repeatable verification command" requirement; rewrite `CommittedConfigSecretShapeTests` to discover `appsettings*.json` files by glob instead of hardcoded `InlineData`. Per strict TDD this must go RED against current `main` before any fix lands. Establishes the enforcement mechanism first so every subsequent PR can point at a real regression test.
2. **PR2 — IsoSwitch fix, ~150-200 lines.** Empty `ConnectionStrings:Postgres` in the base `appsettings.json`; add `RequiredConnectionStringsOptions`/Validator for IsoSwitch mirroring CardVault's; wire `ValidateOnStart()` in `Program.cs`; add `StartupSecretValidationTests`-style fail-fast tests (template exists at `IsoSwitch.Tests/Security/StartupSecretValidationTests.cs`).
3. **PR3 — IsoAudit fix, ~150-200 lines.** Empty `ConnectionStrings:IsoSwitchDb` in both `appsettings.json` and `appsettings.Development.json`; remove the `Program.cs:63` in-code fallback; add an equivalent fail-fast validator (IsoAudit has none of this infrastructure — more net-new code than IsoSwitch).
4. **PR4 — Design-time factory hardening, ~80-120 lines.** All three `IDesignTimeDbContextFactory` classes: remove the weak-default fallback, throw a clear exception naming the required env var when absent, and apply the naming reconciliation from Section 9.1.
5. **PR5 — `.env.example` + docker-compose cleanup, ~60-100 lines.** ADD the missing container-bootstrap vars (`POSTGRES_PASSWORD`, `MSSQL_SA_PASSWORD`) and the (possibly renamed) design-time factory vars to the existing `backend/deploy/.env.example`; move the compose inline passwords to interpolation (`${POSTGRES_PASSWORD}` etc.); fix the duplicate `ConnectionStrings__Postgres` key (Section 4, option a); purge the Kafka `SigningSecret` values and add the detecting gitleaks rule; update the stale `.gitleaks.toml:29-33` comment; resolve `docs/env.example`'s fate. Must resolve the `.env*` permission blocker (Section 9.3) before starting.
6. **PR6 — Full-tree, BLOCKING CI secret-scan job, ~60-100 lines.** Add a full-tree gitleaks scan that blocks every PR — land this LAST, after PR1-5, so it goes green on arrival instead of deadlocking the remediation PRs against itself (Section 5). Do not build it on `gitleaks-action@v2` given its 2026-09-16 sunset; invoke the `gitleaks` CLI directly.

Total ≈ 580-870 changed lines across 6 PRs, each individually within the 400-line budget.

## Affected Areas

- `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json` — inline password (real risk)
- `backend/services/IsoAudit/src/IsoAudit.Api/appsettings.json` — inline password (real risk, service outside original scope)
- `backend/services/IsoAudit/src/IsoAudit.Api/appsettings.Development.json` — inline password (real risk)
- `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs:60-66` — in-code weak fallback
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/IsoSwitchDbContextFactory.cs` — design-time fallback
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/CardVaultDbContextFactory.cs` — design-time fallback
- `backend/services/CardVault/src/CardVault.Infrastructure.Identity/Auth/IdentityAppDbContextFactory.cs` — design-time fallback (new finding)
- `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json` — Kafka `SigningSecret`
- `backend/services/CardVault/src/CardVault.Api/appsettings.Development.json` — Kafka `SigningSecret`
- `backend/deploy/docker-compose.yml` — inline passwords (container bootstrap + CardVault override)
- `backend/deploy/.env.example` — canonical template; needs new vars + duplicate-key fix (already exists, does NOT need creating)
- `docs/env.example` — fate to be decided (delete vs pointer)
- `backend/services/CardVault/tests/CardVault.Tests/Security/CommittedConfigSecretShapeTests.cs` — sole root cause of the broken evidence chain (hardcoded `InlineData` scope)
- `openspec/specs/security-hardening/spec.md:297-333` — SEC-9 amendment (Option A) + new verification-command requirement
- `.gitleaks.toml` — stale comment at lines 29-33; new rule needed for message-signing secrets
- `.github/workflows/ci.yml` — needs the full-tree blocking job; current job is PR-diff-scoped only

## Risks

- gitleaks-action@v2 sunset (2026-09-16) affects the CI slice design (PR6) — do not build the full-tree job on the deprecated action.
- The full-tree blocking job would deadlock the remediation PRs if landed before them — PR6 must land last.
- Test baseline (705/705) was NOT independently re-run during exploration per the no-full-suite-run constraint; the last confirmed value is from the archived `verify-report.md` (2026-07-24). `sdd-apply` must re-establish this baseline before making changes.
- An active permission rule denies `Read`/`Grep`/`Write` on `.env*` paths. `backend/deploy/.env.example` content in Section 4 comes from `git show HEAD:` output. PR5 needs a resolved write mechanism before it can start.

## Ready for Proposal

Yes. Scope (all committed occurrences including Kafka `SigningSecret`), mechanism (env vars + `.env.example` + compose interpolation), CI approach (full-tree, blocking, landed last), and the SEC-9 spec-amendment approach (Option A + new verification-command requirement) are all settled. The remaining open items are Section 9: design-time factory env-var naming, `docs/env.example`'s fate, and the `.env*` permission blocker.

---

## Changelog: v1 → v2

1. **Withdrawn entirely:** the claim that `backend/deploy/.env.example` does not exist and that the docker-compose fresh-clone flow is "already broken". Root cause of the error: `Glob` does not match dotfiles by default, and the claim of absence was asserted without cross-checking via a direct `Read`/`Grep` or `git ls-files`. `.env` (not `.env.example`) being absent from the working tree is normal — it is gitignored by design, and copying the example is the documented onboarding step.
2. **Withdrawn entirely:** the "three-way path drift" framing (`backend/.env.example` vs `backend/deploy/.env.example` vs `docs/env.example`). Reframed: `backend/deploy/.env.example` is unambiguously canonical (named by three independent artifacts, exists, comprehensive); `design.md`'s alternate path is a stale doc typo, not real drift; the only live open question is `docs/env.example`'s fate.
3. **Withdrawn entirely:** root-cause item "the cited evidence file does not exist" in the broken-evidence-chain section. The verify-report's `.env.example` citation was valid. The root cause is now presented as singular and sharper: the hardcoded `[InlineData]` scope in `CommittedConfigSecretShapeTests.cs:76-78`.
4. **Dropped:** the "unresolved risk" asking whether `.env.example` existed on a branch and was later removed — resolved; it never went missing. The v1 uncertainty was itself an artifact of the same Glob/dotfile error.
5. **Added:** Section 4 now documents `.env.example`'s actual contents and a newly investigated latent bug — the duplicate `ConnectionStrings__Postgres` key across the CardVault and IsoSwitch sections — with two remediation options and a recommendation.
6. **Folded in as settled:** scope = ALL occurrences, nothing deferred; mechanism = env vars + `.env.example` + compose interpolation; CI = full-tree BLOCKING scan landed last (reasoning now explicit and load-bearing); spec = amend SEC-9 in place AND add a new repeatable-verification-command requirement.
7. **Kafka `SigningSecret` resolved as IN SCOPE** with SEC-9 category extension plus a new gitleaks rule — this overrides the exploration's own initial recommendation to treat it as a documented exclusion.
8. **Re-estimated PR5** down from ~100-150 to ~60-100 lines, and reworded from "create the canonical file" to "add missing vars + fix duplicate key + interpolate compose + update stale comment + resolve docs/env.example", reflecting that the canonical file already exists. Total slice estimate revised from ~620-1020 to ~580-870 lines.
9. **Added the `.env*` permission blocker** as an explicit open item (Section 9.3) and a risk — discovered while verifying the v1 error.
10. **Everything else** (Sections 1, 2, 5's CI-scoping and deprecation findings, the `IdentityAppDbContextFactory` new finding, the `CommittedConfigSecretShapeTests` diagnosis) is unchanged from v1 — independently confirmed correct and not dependent on the Glob/dotfile error.
