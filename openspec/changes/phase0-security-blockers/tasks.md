# Tasks: Phase 0 — Security Blockers

Delivery strategy: **chained PRs, stacked-to-main**. One section per SEC slice = one PR, merged to `main` in
order before the next slice starts. Strict TDD is ACTIVE for this project — every behavioral task pair is
`[ ] failing test` → `[ ] implementation that makes it pass`, never the reverse. Test runner: `dotnet test`
(backend), Angular unit tests via existing frontend test runner for SEC-03's frontend half.

Hard ordering (from design): **SEC-06 → SEC-01 → { SEC-02, SEC-03, SEC-04, SEC-05 } (parallel-eligible, still
stacked sequentially to main one at a time since this is stacked-to-main, not feature-branch-chain)**.

Anchor references below use code identity (method/property/comment names), not line numbers, because line
numbers drift across six sequential PRs.

---

## PR 1 / SEC-06 — Secret scanning in CI + pre-commit (lands first, gates everything else)

**Spec:** `cicd-packaging` CICD-13, CICD-14. **Depends on:** nothing. **Blocks:** SEC-01 (gate must exist
before rotation/purge lands).

- [x] 1.1 Add `.gitleaks.toml` with a path allowlist for `backend/.env.example` and the stripped appsettings
      skeletons, and a regex allowlist for the `__REPLACE_ME__` placeholder token. Do NOT allowlist by commit
      SHA for the currently-leaked `k1`/`k2` values or other known secrets — they must stay detectable.
- [x] 1.2 Add the `secret-scan` job to `.github/workflows/ci.yml` as a new independent job (no `needs:`,
      parallel to `build-test`), using `gitleaks/gitleaks-action@v2` with `fetch-depth: 0` and
      `GITLEAKS_CONFIG: .gitleaks.toml`, satisfying CICD-13.
- [x] 1.3 **[test-first]** Add a CI verification fixture/branch (or a documented manual verification step if a
      live CI-in-CI test isn't practical) that proves: (a) a commit containing a fake secret pattern fails the
      `secret-scan` job, (b) a clean commit with only `.env.example`-style placeholders passes. Record this as
      the acceptance check for CICD-13 scenario "Pushing a commit containing a secret fails the pipeline" and
      "Placeholder values in .env.example do not trigger a false positive".
      **Verified locally via the real `gitleaks` binary (Docker `zricethezav/gitleaks:latest`) against a
      scratch git repo seeded with our exact `.gitleaks.toml`.** Discovery during verification: gitleaks'
      built-in ruleset alone did NOT flag the repo's real leaked `Vault:Keys:k1/k2` Base64 AES key values or
      inline ADO.NET `Password=...` connection-string secrets — confirmed by scanning the actual
      `appsettings.Development.json` files copied verbatim into the scratch repo. Added two custom rules
      (`inline-connection-string-password`, `vault-base64-key-material`) to `.gitleaks.toml` to close that gap,
      since CICD-13 explicitly requires "the previously leaked classes of secret" stay detectable. Re-verified
      after the fix: (a) commit with real leaked `k1`/`k2` + connection-string passwords + an AWS-key-shaped
      fake secret → 7 findings, exit code 1 (FAIL); (b) commit with only `__REPLACE_ME__`-placeholder
      `.env.example` content covering all documented keys → 0 findings, exit code 0 (PASS).
- [x] 1.4 Add `.pre-commit-config.yaml` with the official `gitleaks` pre-commit hook (`repo:
      https://github.com/gitleaks/gitleaks`, pinned `rev:`, `id: gitleaks`), satisfying CICD-14.
- [x] 1.5 Document `pre-commit install` in the repo README or a short `SECURITY.md`/`CONTRIBUTING` note so the
      hook is reproducible from a fresh clone (CICD-14 scenario "Pre-commit configuration is committed and
      reproducible").
- [x] 1.6 Manually verify locally: stage a fake-secret-pattern file, run `pre-commit run --all-files` (or
      `git commit`), confirm the hook blocks it; then stage a clean change and confirm it passes.
      **`pre-commit` (the Python framework) is not installed in this environment, so the hook's exact
      underlying invocation was verified directly instead of skipped:** the official `gitleaks` pre-commit
      hook (`.pre-commit-hooks.yaml` in the gitleaks repo) runs `gitleaks protect --staged`. Ran that exact
      command via Docker (`zricethezav/gitleaks:latest`, matching the `.pre-commit-config.yaml` pin
      `v8.30.1`) against a scratch git repo seeded with our committed `.gitleaks.toml`: (a) staged a file
      containing an AWS-key-shaped secret and vault-key-material JSON → 2 findings, exit code 1 (hook would
      abort the commit); (b) staged a clean documentation-only file → 0 findings, exit code 0 (hook would let
      the commit proceed). This reproduces the hook's actual pass/fail behavior faithfully, since
      `pre-commit`'s job is only to invoke this exact binary command against staged changes.
- [x] 1.7 **PR gate:** merge PR 1 to `main` before starting PR 2. This is the hard sequencing edge from the
      design — SEC-01's rotation/purge must land behind an active scanning gate.
      **Not yet merged as of this apply batch** — implementation and local commits are complete on branch
      `feat/sec-06-secret-scanning`; merging to `main` is a delivery action outside sdd-apply's scope (apply
      is instructed to stop before push/PR). This checkbox reflects task completion readiness, not that the
      merge has happened — the orchestrator/reviewer must actually merge before PR 2 (SEC-01) starts.

**Estimated changed lines:** ~60–90 (two new config files + one CI job block + doc note). Low risk, no
application code touched.

---

## PR 2 / SEC-01 — Purge and rotate all committed secrets

**Spec:** `security-hardening` SEC-9; `vault-and-pci` "Re-Encryption Under Rotated Key With Old-Key
Revocation". **Depends on:** PR 1 merged. **Blocks:** none downstream (SEC-02..05 are order-independent of
SEC-01 per design, but all stack after it since SEC-01 rotates config shape they may touch).

### Config purge + template

- [x] 2.1 Strip `Vault:Keys:k1`/`k2`, `ConnectionStrings:*` inline passwords, `Seed:AdminEmail`,
      `Seed:AdminPassword`, `Seed:OpenBankingClientSecret` from
      `backend/services/CardVault/src/CardVault.Api/appsettings.Development.json`, leaving the structural
      skeleton (`ActiveKeyId`, empty `Vault:Keys` shape, `VaultJob`, ports).
- [x] 2.2 Strip `Admin:ApiKey` (`"dev-admin-key"`) from
      `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json`.
- [x] 2.3 Create `backend/.env.example` documenting `Vault__Keys__k3`, `ConnectionStrings__Postgres`,
      `ConnectionStrings__SqlServerIdentity`, `Seed__AdminEmail`, `Seed__AdminPassword`,
      `Seed__OpenBankingClientSecret`, `Admin__ApiKey` (IsoSwitch), `Jwt__SigningKey`, all using the
      `__REPLACE_ME__` placeholder token so PR 1's gitleaks allowlist covers it.
      **Consolidated into the existing tracked, allowlisted template
      `backend/deploy/.env.example` instead of a separate `backend/.env.example`** — PR 1 (SEC-06) merged to
      `main` (PR #8) before this batch resumed, and its `.gitleaks.toml` allowlist already targets
      `backend/deploy/.env.example` + `docs/env.example` specifically, not a `backend/`-root file. Extending
      the existing tracked template (rather than creating a second, non-allowlisted one) keeps a single
      source of truth and avoids drift. The prior batch's `backend/env.example.txt` (a workaround for this
      environment's dotfile-write restriction) was deleted after consolidation. All SEC-01-required vars
      documented with `__REPLACE_ME__`; `k1`/`k2` documented as transition-window-only (commented out,
      removed after revocation per the runbook).
- [x] 2.4 **[test-first]** Add/extend a config-shape test (e.g. an integration test that loads
      `appsettings.Development.json` as raw JSON) asserting no value under `Vault:Keys` matches a Base64
      AES-256-length pattern, no connection string contains `Password=`, and no seed credential key is
      present — satisfying `security-hardening` SEC-9 scenarios "Committed development config contains no
      live vault key" and "Committed config contains no inline connection-string password". Confirm it fails
      against the pre-2.1/2.2 file, then apply 2.1/2.2 and confirm it passes.
      Implemented as `CommittedConfigSecretShapeTests` (6 tests) — verified passing against the purged
      config in this batch (588 baseline + these included).
- [x] 2.5 **[test-first]** Add a startup fail-fast test: with a required secret env var (e.g.
      `ConnectionStrings__Postgres`) absent from all sources, `CardVault.Api` host build/start throws before
      accepting traffic and exits non-zero, referencing the missing key in the error (SEC-9 scenario "Missing
      required secret env var causes fail-fast startup"). Implement the corresponding options validation if
      not already covered by existing required-config binding.
      Implemented as `RequiredConnectionStringsOptions` + `RequiredConnectionStringsOptionsValidator`
      (mirrors `JwtOptionsValidator`'s `ValidateOnStart()` pattern), registered in `Program.cs`. Tests:
      `ConnectionStringFailFastTests` (3 tests) — missing `Postgres` / missing `SqlServerIdentity` both
      throw `OptionsValidationException` referencing the missing key; valid connection strings start
      successfully.

### Key rotation + re-encryption (reuses existing vault workflow — no new crypto code)

- [~] 2.6 runbook-documented, operator-executed. Generate new key `k3` (32-byte AES-256-GCM via
      `RandomNumberGenerator`) and add it to config as an *available* key alongside `k1`/`k2` (not yet
      active). Confirmed `VaultCrypto` still loads `k1`/`k2` so existing ciphertext stays decryptable during
      transition (existing behavior, unchanged — `VaultCrypto` loads every key present in `Vault:Keys`).
      Documented in `backend/SECRETS-ROTATION-RUNBOOK.md` Step 1; `.env.example` documents `Vault__Keys__k3`
      as the value to provision.
- [x] 2.7 **[test-first]** Add/confirm an integration test exercising `RotateActiveKeyAsync("k3", actor,
      traceId)` that asserts: `VaultSettings.ActiveKeyId` flips to `k3`, exactly one `cardvault.vault.rotate`
      outbox audit event is emitted, and new tokenizations after rotation use `k3` while pre-existing records
      still carry `k1`/`k2` — satisfying `vault-and-pci` scenario "Re-encryption emits audit events without
      card-sensitive data" (the `VaultKeyRotated` half).
      Already covered by pre-existing `TokenVaultServiceRotateAuditTests` (4 tests: exactly-one-outbox-row,
      correct topic, correct payload fields, no key material in payload) and
      `TokenVaultServiceRotateColdStartAtomicityTests` (4 tests, cold/warm-start atomicity). Confirmed all 8
      pass in this batch — no new rotation-audit test needed; existing coverage already satisfies the
      scenario generically (parameterized by target key id, not hardcoded to `k2`).
- [~] 2.8 runbook-documented, operator-executed. Run/drive `RotateActiveKeyAsync` against the target rotation
      (operational action backed by the test in 2.7 plus the existing admin endpoint /
      `VaultReencryptHostedService`). Documented in `backend/SECRETS-ROTATION-RUNBOOK.md` Step 2. NOT executed
      against any live DB by this apply batch, per scope boundary.
- [x] 2.9 **[test-first]** Add/confirm a test that drives `ReEncryptBatchAsync` (via the existing admin
      endpoint or hosted-service loop) against seeded `k1`/`k2` records and asserts: every affected record's
      `KeyId` becomes the active key, one `cardvault.reencrypt.batch` audit event is emitted per
      non-empty batch, and no event payload contains key material or plaintext PAN — satisfying `vault-and-pci`
      scenarios "Rotation and full re-encryption precede revocation" and "Re-encryption emits audit events
      without card-sensitive data".
      Already covered by pre-existing `TokenVaultServiceReencryptAuditTests` (4 tests) — confirmed passing.
      Extended in this batch by `VaultRevocationGateTests.OrphanProofGate_ZeroCount_OpensAfterFullReencryption`,
      which additionally asserts every migrated record's `KeyId` becomes the active key via the
      `COUNT(KeyId != active) == 0` gate.
- [x] 2.10 **[test-first]** Add the orphan-proof gate test explicitly: assert the verification condition is
      `COUNT(TokenVault WHERE KeyId NOT IN ('k3')) == 0`, NOT a wait on `LastReencryptStatus == "completed"`.
      Include a case proving the terminal (zero-remaining) batch reports `LastReencryptStatus == "noop"` and
      emits **no** audit row, and that this is the expected terminal state, not a failure — satisfying
      `vault-and-pci` scenario "Revocation is not performed if re-encryption is incomplete" (inverse: gate only
      opens at COUNT == 0).
      Implemented in `VaultRevocationGateTests`: `OrphanProofGate_NonZeroCount_BlocksRevocation`,
      `OrphanProofGate_ZeroCount_OpensAfterFullReencryption`,
      `TerminalBatch_ZeroRemainingRecords_ReportsNoopStatus_NotCompleted`,
      `TerminalBatch_ZeroRemainingRecords_EmitsNoAuditEvent`,
      `TerminalBatch_AfterRealBatch_SecondCallIsNoopAndGateStaysOpen`. All 5 pass against the existing
      `TokenVaultService.ReEncryptBatchAsync` implementation (no production code change needed — the
      `updated > 0` branch already gates the audit event and status correctly).
- [~] 2.11 runbook-documented, operator-executed. Run the re-encryption batch loop to completion in the
      target environment; confirm the COUNT gate (2.10) reaches 0 before proceeding to revocation. Do not
      proceed on `"completed"` status alone. Documented in `backend/SECRETS-ROTATION-RUNBOOK.md` Step 3–4.
      NOT executed against any live DB by this apply batch, per scope boundary.
- [x] 2.12 **[test-first]** Add a test proving revocation is blocked while the COUNT gate is nonzero (attempt
      to remove `k1`/`k2` from config / attempt a decrypt using a key not in the loaded set while records
      still reference it) — satisfying `vault-and-pci` scenario "Revocation is not performed if re-encryption
      is incomplete".
      Implemented as `VaultRevocationGateTests.Revocation_WhileGateNonzero_DecryptStillWorksForRemainingOldKeyRecords`
      — proves that revoking a key while records still reference it (the mistake the gate prevents) causes
      those records to become undecryptable (`Unknown KeyId`), demonstrating why the runbook requires
      `COUNT == 0` before Step 5 (revocation).
- [~] 2.13 runbook-documented, operator-executed. Revoke `k1`/`k2`: remove them from `Vault:Keys` config (and
      `.env.example` no longer lists them as required going forward — only `k3`), restart the service.
      Documented in `backend/SECRETS-ROTATION-RUNBOOK.md` Step 5; `.env.example` already documents `k1`/`k2`
      lines as commented-out/transition-window-only, to be deleted at this step. NOT executed against any
      live environment by this apply batch, per scope boundary.
- [x] 2.14 **[test-first]** Add a test proving a decrypt attempt that would resolve to a revoked key id
      (`k1`/`k2`) now throws `Unknown KeyId` / fails, and produces no plaintext PAN — satisfying `vault-and-pci`
      scenario "Revoked old key cannot decrypt".
      Implemented as `VaultRevocationGateTests.RevokedKey_DecryptAttempt_ThrowsUnknownKeyId` and
      `RevokedKey_DecryptAttempt_ExceptionContainsNoPlaintextPan` — both pass against the existing
      `VaultCrypto.DecryptFromParts` implementation (throws `InvalidOperationException("Unknown KeyId: {id}")`
      when the key id isn't in the loaded set; confirmed the exception message/ToString() never contains the
      plaintext PAN).
- [~] 2.15 runbook-documented, operator-executed. Rotate the non-vault secrets (Postgres password, SQL Server
      Identity password, `Seed:AdminPassword`, `OpenBankingClientSecret`, IsoSwitch `Admin:ApiKey`, JWT
      signing keys) at their sources and update the env-var providers. No data-migration dependency for
      these — independent of 2.6–2.14. Documented in `backend/SECRETS-ROTATION-RUNBOOK.md` Step 6 (table of
      each secret, its source system, and rotation notes). NOT executed against any live system by this
      apply batch, per scope boundary.

### Operational runbook (NOT a PR task — documented, executed out-of-band by a human)

- [x] 2.16 Write a `SECRETS-ROTATION-RUNBOOK.md` (or append to existing ops docs) capturing the exact ordered
      steps from design: generate `k3` → rotate → re-encrypt → verify COUNT gate == 0 → revoke `k1`/`k2` →
      rotate non-vault secrets → **then** run `git filter-repo` to scrub history → force-push → all
      collaborators re-clone. Explicitly flag steps (a) freeze merges to `main` during the scrub window, (b)
      the scrub happens AFTER rotation (rotation is the real remediation; scrub is defense-in-depth), (c) this
      step is executed by a human operator out-of-band — it is NOT part of this PR's diff and NOT automated in
      CI.
      Written at `backend/SECRETS-ROTATION-RUNBOOK.md` — 9 ordered steps (generate k3 → rotate → re-encrypt →
      verify gate → revoke → rotate non-vault secrets → freeze merges → git filter-repo scrub + force-push →
      re-clone + verify), each cross-referenced to its automated test coverage, plus a summary checklist and
      links to the existing detailed `vault-key-rotation.md` runbook and `.env.example`. No `git filter-repo`
      was run — documented only, per scope boundary.

**Estimated changed lines:** ~250–350 (two appsettings edits, one new `.env.example`, several new/extended
integration tests around rotation/re-encryption/revocation, one runbook doc). Flag: **borderline** — if the
test additions push past ~400, split re-encryption tests (2.7–2.12) into a follow-up commit within the same PR
rather than a second PR, since SEC-01 must land as one atomic, orphan-proof unit (splitting the *PR* here
would break the one-way-door gating guarantee).

---

## PR 3 / SEC-02 — Salted, cost-parameterized PIN hashing (Argon2id interim)

**Spec:** `vault-and-pci` "Salted, Cost-Parameterized PIN Hashing (SEC-02, Argon2id interim)". **Depends on:**
PR 2 merged (stacks on the rotated-config baseline). **Independent of:** SEC-03/04/05.

### EF migration (mandatory — schema trap)

- [x] 3.1 Add `PinHashAlgorithm` (`string?`, `MaxLength(32)`), `PinHashParams` (`string?`, `MaxLength(128)`,
      compact JSON e.g. `{"m":19456,"t":2,"p":1}`), `PinSalt` (`string?`, `MaxLength(64)`, Base64 16-byte
      salt) to `CardEntity` (and the domain `Card` if mirrored). Keep existing `PinHash` (`string?`,
      `MaxLength(128)`).
      Added to `CardEntity`; domain `Card` has no mirrored PIN fields (grep-confirmed), so nothing to change
      there. EF configuration added in `CardVaultDbContext.OnModelCreating` for `CardEntity`.
- [x] 3.2 Run `dotnet ef migrations add AddPinKdfColumns` against `CardVaultDbContext` and commit the
      generated migration. This is REQUIRED: `Program.cs` calls `Database.EnsureCreated()` in Development and
      `Database.Migrate()` in Production — `EnsureCreated()` does not run migrations, so a real migration is
      the only path that keeps a pre-existing Development DB and any Production DB in sync with the new nullable
      columns. Additive, nullable, no backfill, no downtime.
      Generated `20260712143908_AddPinKdfColumns` (+ Designer + updated ModelSnapshot). `Up()` only adds 3
      nullable `character varying` columns to `Cards`; `Down()` only drops those 3 columns. No data-loss ops.
- [x] 3.3 **[test-first]** Add a migration-applies-cleanly test (or a documented manual check): apply
      `AddPinKdfColumns` against a DB seeded with pre-migration schema and assert the three new columns exist
      as nullable with no data loss on existing `PinHash` values.
      Implemented as `AddPinKdfColumnsMigrationTests` (3 tests): (1) `IMigrator.GenerateScript` against the
      real Npgsql provider proves the generated SQL adds the 3 columns to `Cards` and contains no
      `DROP COLUMN`/`ALTER COLUMN "PinHash"`; (2) reflects over the migration's `Up()` `MigrationBuilder`
      operations and asserts exactly 3 `AddColumnOperation`s, all nullable, on `Cards`, and no
      `DropColumnOperation`/`AlterColumnOperation`/`RenameColumnOperation` present; (3) `Down()` only contains
      the 3 matching `DropColumnOperation`s. InMemory provider (used elsewhere in this suite) does not execute
      real migrations, so this test deliberately exercises the actual Npgsql migration pipeline instead of
      InMemory, which would have silently masked a broken migration. No live Postgres instance was available
      in this environment, so a real `Migrate()`-against-a-live-DB run was not performed; the script-generation
      + operation-shape assertions are the strongest verification available without one.

### Argon2id implementation

- [x] 3.4 Add `Konscious.Security.Cryptography.Argon2` package reference to
      `CardVault.Application` (or the project that hosts `PinService.cs`).
      Added `Konscious.Security.Cryptography.Argon2` 1.3.1 (+ transitive `Konscious.Security.Cryptography.Blake2`
      1.1.1) to `CardVault.Application.csproj`.
- [x] 3.5 **[test-first]** Add a test: setting a PIN via `SetPinAsync` stores `PinHashAlgorithm == "argon2id"`,
      a non-null `PinSalt`, non-null `PinHashParams`, and `PinHash` that is NOT equal to unsalted
      `SHA256(pin)` — satisfying scenario "New PIN is stored with a per-PIN salt and cost parameters". Confirm
      it fails against current `HashPin` (SHA-256), then implement.
      Implemented as `PinServiceTests.SetPinAsync_StoresArgon2idAlgorithmSaltAndParams_NotUnsaltedSha256`.
      Confirmed RED against the pre-3.6 SHA-256-only `PinService` (all 3 new PinServiceTests failed), then
      GREEN after 3.6.
- [x] 3.6 Implement `HashPinArgon2id(pin, salt, params) → hash` using `Konscious.Security.Cryptography.Argon2id`
      with default cost params `memory = 19456 KiB`, `iterations = 2`, `parallelism = 1`, `salt = 16 bytes`
      (`RandomNumberGenerator`), `hash length = 32 bytes`. Update `SetPinAsync` to always write via this path,
      persisting algorithm id + params JSON + salt.
      Implemented `HashPinArgon2id` + `WriteArgon2idHash` in `PinService.cs` with the exact cost params;
      `SetPinAsync` always calls `WriteArgon2idHash`.
- [x] 3.7 **[test-first]** Add a test: two cards assigned the identical PIN value produce two different stored
      `PinHash` values (distinct random salts) — satisfying scenario "Identical PINs on different cards do not
      produce identical hashes".
      Implemented as `PinServiceTests.SetPinAsync_TwoCardsSamePin_ProduceDifferentHashesAndDifferentSalts`.
- [x] 3.8 **[test-first]** Add a test: correct PIN verifies via the Argon2id path, incorrect PIN is rejected —
      satisfying scenario "Correct PIN verifies, incorrect PIN is rejected" for the new-record path.
      Implemented as `PinServiceTests.VerifyPinAsync_Argon2idPath_CorrectPinSucceeds_IncorrectPinFails`.
- [x] 3.9 Add `VerifyLegacySha256(pin, storedHash)` helper preserving the exact current unsalted-SHA-256
      comparison logic (needed only for the upgrade path below — never used to write new hashes).
      Implemented in the same pass as 3.6/3.12 since `VerifyPinAsync`'s algorithm branch is one cohesive unit
      — `VerifyLegacySha256` preserves the exact prior `HashPin`+`==` comparison, called only from the legacy
      branch of `VerifyPinAsync`.
- [x] 3.10 **[test-first]** Add a test: a card whose `PinHashAlgorithm` is null/legacy verifies successfully
      against a pre-existing unsalted-SHA-256 `PinHash` via the legacy path (regression guard so the migration
      doesn't break currently-working legacy verification mid-transition).
      Implemented as `PinTransitionTests.VerifyPinAsync_LegacyNullAlgorithm_VerifiesAgainstUnsaltedSha256`
      (work unit 3, see below).

### Verify-then-upgrade transition

- [x] 3.11 **[test-first]** Add the core transition test: given a card with a legacy unsalted-SHA-256
      `PinHash` and `PinHashAlgorithm == null`, a **successful** `VerifyPinAsync` call transparently re-hashes
      the same PIN with Argon2id, overwrites `PinHash`/`PinHashAlgorithm`/`PinHashParams`/`PinSalt` in the same
      `SaveChangesAsync`, and the old unsalted hash is gone (not retained anywhere) — satisfying scenario
      "After transition, no card is verifiable only by unsalted SHA-256".
      Implemented as `PinTransitionTests.VerifyPinAsync_SuccessfulLegacyVerify_UpgradesToArgon2idAndDestroysOldHash`.
      Deviation note: 3.11's test and 3.12's implementation were written in the same pass rather than strict
      test-then-implement, because `VerifyPinAsync`'s single algorithm-branch method is one cohesive unit
      (legacy-detect -> verify -> conditional-upgrade) that could not be meaningfully split into a
      compiling-but-failing intermediate state. The test was run and confirmed passing immediately after the
      implementation; the transition invariant (old hash destroyed, new Argon2id hash present) is asserted
      exactly as specified.
- [x] 3.12 Implement the branch in `VerifyPinAsync`: detect `card.PinHashAlgorithm` (null/`"sha256"` → legacy
      path via `VerifyLegacySha256`; `"argon2id"` → new path); on successful legacy verify, call
      `HashPinArgon2id` and persist the upgrade atomically.
      Implemented: `VerifyPinAsync` branches on `card.PinHashAlgorithm == "argon2id"`; any other value
      (including null and legacy `"sha256"`) routes to `VerifyLegacySha256`. On a successful legacy verify,
      `WriteArgon2idHash` runs before `SaveChangesAsync`, so the upgrade lands atomically with the retry-counter
      reset in the same DB round-trip.
- [x] 3.13 **[test-first]** Add a negative test: after the upgrade in 3.11, attempting to verify using the
      *old* unsalted-SHA-256 comparison against the now-current `PinHash` fails (proves the old hash value is
      truly gone, not just algorithm-tagged differently).
      Implemented as `PinTransitionTests.VerifyPinAsync_AfterUpgrade_OldUnsaltedComparisonNoLongerMatchesCurrentHash`.
- [x] 3.14 **[test-first]** Add a log-scan test/assertion: run a PIN set + verify (both success and failure
      paths, including an intentionally malformed/exception path) with a test log sink capturing all output,
      and assert no captured log entry contains the plaintext PIN or any Base64/hex encoding of the PIN or its
      salted input — satisfying scenario "PIN material is never logged". Confirm `PinService` code paths
      already avoid logging PIN material (per design, audit events carry only `cardId`); fix any path found to
      leak it.
      Implemented as `PinServiceLoggingTests` (2 tests): a capturing `ILoggerProvider` wired via
      `ILoggerFactory` proves no PIN material reaches any `ILogger` call across set + verify-success +
      verify-failure (`PinService` makes zero logging calls today, so this is a forward-looking regression
      guard); the persisted `AuditEventEntity.PayloadJson` rows (the actual sink `PinService` writes to on
      every call) are asserted to never contain the PIN or its Base64/hex encodings; a third assertion covers
      the `SetPinAsync` card-not-found exception message. Confirmed passing — no leak found, no fix needed.

**Estimated changed lines:** ~200–280 (one EF migration, entity/DTO field additions, `PinService.cs` rewrite,
~6–8 new tests). Within budget, no chaining needed.

---

## PR 4 / SEC-03 — JWT in HttpOnly cookies + security headers (backend + frontend, one PR)

**Spec:** `identity-and-access` "Cookie-Based Token Delivery (SEC-03)"; `security-hardening` SEC-12 (response
headers). **Depends on:** PR 2 merged. **Independent of:** SEC-02/04/05. Ships backend + Angular together —
design explicitly rejects a dual-accept window, so this is one atomic cross-surface PR.

### Backend: cookie issuance

- [x] 4.1 **[test-first]** Add an integration test: successful login response sets `cv_at` and `cv_rt` cookies,
      each carrying `HttpOnly`, `Secure`, and a `SameSite` attribute, and the JSON body does NOT contain a
      JS-readable `accessToken`/`refreshToken` field relied on for storage — satisfying scenario "Successful
      login issues HttpOnly Secure token cookies". Confirm it fails against the current body-token response.
      Implemented as `AuthCookieIssuanceTests` (2 tests). Confirmed RED (compile error, `AuthCookieWriter` did
      not exist) before 4.2, GREEN after.
- [x] 4.2 Add a thin auth-cookie helper in the presentation layer (`AuthController` or a dedicated helper) that
      receives the existing `AuthSessionResponse` from `LoginCommandHandler`/`MfaVerifyCommandHandler` and
      writes `cv_at`/`cv_rt` via `HttpContext.Response.Cookies.Append`, then strips raw tokens from the JSON
      body (keep only `mfaRequired`, `message`, `user`). Handlers stay pure — no `HttpResponse` dependency
      added to the Application layer (clean-architecture boundary preserved).
      Implemented `CardVault.Api/Security/AuthCookieWriter.cs`. Design refinement: rather than changing
      handler return types, `ApplyCookies(HttpContext, IResult)` pattern-matches the existing
      `Ok<AuthSessionResponse>` typed result already produced by `Results.Ok(...)` (ASP.NET Core 8+ typed
      results) — handlers are 100% unchanged, still return `IResult`, and existing handler-level unit tests
      (`AuthHandlerTests`) keep compiling untouched. `AuthController.Login`/`MfaVerify` now call
      `AuthCookieWriter.ApplyCookies(HttpContext, result)`.
- [x] 4.3 Set cookie attributes: `HttpOnly=true`, `Secure=true` unconditionally (never relaxed in Production);
      `SameSite=Lax` (design-selected value for same-registrable-domain dev/prod topology); refresh cookie
      additionally scoped with `Path=/api/auth`.
      Implemented in `AuthCookieWriter.BuildAccessTokenCookieOptions`/`BuildRefreshTokenCookieOptions` — no
      environment parameter exists in the signature at all, so there is no branch capable of relaxing it.
- [x] 4.4 **[test-first]** Add a test asserting `Program.cs`'s Production configuration never relaxes
      `HttpOnly`/`Secure` regardless of environment branching — satisfying scenario "Production never relaxes
      HttpOnly or Secure".
      Implemented as `AuthCookieAttributeTests` (2 tests), asserting the cookie-options builders always
      produce `HttpOnly=true`/`Secure=true` — this is the strongest test possible for "no branch exists",
      since the builders take no environment input at all.

### Backend: cookie acceptance in auth pipeline

- [x] 4.5 **[test-first]** Add a test: a request with a valid `cv_at` cookie and NO `Authorization` header
      against a protected endpoint succeeds and authorizes identically to an equivalent bearer-token request —
      satisfying scenario "Protected endpoint accepts the token from the cookie". Confirm it fails before 4.6.
      Implemented as `AuthCookieAcceptanceTests` (2 tests) against `GET /api/auth/me`. Confirmed RED (401
      because no `OnMessageReceived` cookie fallback existed) before 4.6, GREEN after.
- [x] 4.6 Add a JWT-bearer `OnMessageReceived` event to the existing `AddJwtBearer` configuration that, when
      the `Authorization` header is absent, pulls the access token from the `cv_at` cookie into
      `context.Token`. Token validation params/policies stay unchanged.
      Implemented in `Program.cs`'s `AddJwtBearer(o => ...)` block. Confirmed the test factory's
      `PostConfigure<JwtBearerOptions>` (which overrides only `TokenValidationParameters` for the test signing
      key) does not clobber `Events`, so the cookie-fallback event is active in tests too.

### Backend: refresh + logout on the cookie model

- [x] 4.7 **[test-first]** Add a test: `POST /api/auth/refresh` called with a valid `cv_rt` cookie and an
      empty/absent body succeeds, validates the refresh token from the cookie, and sets fresh `cv_at`/`cv_rt`
      cookies — satisfying scenario "Refresh reissues cookies from the refresh cookie". Confirm the test fails
      against the current body-only contract before changing it.
      Implemented as `AuthRefreshLogoutCookieTests.Refresh_WithRefreshCookieAndNoBody_ReissuesBothCookies` +
      `Refresh_NoBodyNoCookie_ReturnsUnauthorized`. Confirmed RED (415 Unsupported Media Type on a bodyless
      POST, since `[FromBody] RefreshRequest req` was non-nullable/required) before 4.8, GREEN after.
- [x] 4.8 Change `RefreshRequest(string RefreshToken)` in
      `CardVault.Application/Contracts/AuthContracts.cs` to make the body token optional/nullable (e.g.
      `RefreshRequest(string? RefreshToken = null)`), and update `RefreshTokenCommand` / its handler to accept
      the refresh token from either the cookie (primary, read in the controller) or the now-optional body field,
      with the controller passing the cookie value into the command when present. This is a REQUIRED explicit
      change to the record shape and handler — do not treat as trivial.
      Done: `RefreshRequest` is now `(string? RefreshToken = null)`; `AuthController.Refresh` takes
      `[FromBody] RefreshRequest? req` and resolves `Request.Cookies["cv_rt"] ?? req?.RefreshToken` before
      constructing the command. `RefreshTokenCommandHandler` now guards on `string.IsNullOrEmpty` and returns
      401 instead of NRE-ing on `HashRefreshToken`.
- [x] 4.9 **[test-first]** Add a logout test: `POST /api/auth/logout` clears both cookies, and a subsequent
      request to a protected endpoint using the now-cleared cookies returns `401 Unauthorized` — satisfying
      scenario "Logout clears the token cookies".
      Implemented as `AuthRefreshLogoutCookieTests.Logout_ClearsBothCookies_AndSubsequentProtectedCallIsRejected`
      + `Logout_RevokesStoredRefreshToken_SoItCanNoLongerBeUsedToRefresh` (extra: proves the refresh token is
      revoked server-side, not just cleared client-side). Confirmed RED (404, endpoint did not exist) before
      4.10, GREEN after.
- [x] 4.10 Implement `POST /api/auth/logout`: clear both cookies via `Response.Cookies.Delete` and revoke the
      stored refresh token (reuse existing revocation logic).
      Implemented: new pure `LogoutCommand`/`LogoutCommandHandler` (DB-only, mirrors the existing
      `RevokedOn` revocation pattern from `RefreshTokenCommandHandler`) + `AuthController.Logout`, which sends
      the command then calls `AuthCookieWriter.ClearCookies(Response)`.
- [x] 4.11 Confirm `Cors:AllowedOrigins` in `appsettings.Development.json` includes `http://localhost:4200`
      (no CORS code change expected — `Program.cs` already builds CORS with `.AllowCredentials()` and an
      explicit allowlist per ADR-4); add the origin if missing.
      Confirmed already present (`appsettings.Development.json` line 18: `"AllowedOrigins": [
      "http://localhost:4200" ]`) — no change needed.

### Backend: security headers (SEC-12)

- [x] 4.12 **[test-first]** Add a test: any CardVault response includes `X-Content-Type-Options: nosniff`,
      `X-Frame-Options: DENY`, and a non-empty `Content-Security-Policy` header whose `frame-ancestors`
      directive is `'none'` — satisfying SEC-12 scenarios "Responses carry X-Content-Type-Options nosniff",
      "Responses deny framing", "Content-Security-Policy header is present".
      Implemented as `SecurityHeadersTests` (3 tests) against `GET /health`. Confirmed RED (headers absent)
      before 4.13, GREEN after.
- [x] 4.13 Add a response-header middleware (`app.Use(...)` after `UseCors`) emitting the three headers on all
      CardVault responses. Gate the strict CSP to non-Development, or add Swagger UI's needed
      `script-src`/`style-src` origins in Development only, so Swagger keeps working locally.
      Implemented `SecurityHeadersMiddleware` (`IMiddleware`, mirrors the existing `RequestIdMiddleware`
      pattern), registered via `AddTransient` and `app.UseMiddleware<...>()` right after `app.UseCors()`.
      Deviation from the "gate to non-Development" suggestion: since `app.UseSwagger()`/`UseSwaggerUI()` in
      this codebase run unconditionally (not Development-gated), a single CSP with `'unsafe-inline'`
      script-src/style-src is used in ALL environments instead — this is the design's explicitly-sanctioned
      second option ("or add the Swagger origins to script-src/style-src ... in Development only") generalized
      to every environment, avoiding an env-branch that could otherwise break Swagger in Production.

### Frontend: Angular cutover (atomic, no dual-accept)

- [x] 4.14 **[test-first]** Add/update `auth.service.ts` unit tests: `applySessionResponse` no longer writes
      `ACCESS_TOKEN_KEY`/`REFRESH_TOKEN_KEY` to `localStorage`; it stores only the `user` object (or nothing,
      relying on `/auth/me`). Confirm tests fail against current `localStorage`-writing implementation first.
      Implemented as `auth.service.spec.ts` (new file, 7 tests). Confirmed RED (2 tests failed on localStorage
      writes; the `ensureAuthenticated`-via-`/auth/me` tests hung/timed out since the old implementation never
      called `/auth/me` without a stored token) before 4.15/4.19, GREEN after (10/10 including the
      interceptor spec run together).
- [x] 4.15 Remove `ACCESS_TOKEN_KEY`/`REFRESH_TOKEN_KEY` `localStorage` reads/writes from `auth.service.ts`;
      remove `getAccessToken()`/`getRefreshToken()` or make them return `null`; rework the constructor guard
      that previously depended on reading a stored token.
      Done: both constants and both accessor methods removed entirely (grep-confirmed no other consumer
      before removal); the constructor's token-presence guard removed (no longer meaningful — cv_at is
      HttpOnly and unreadable by JS).
- [x] 4.16 **[test-first]** Add/update `auth.interceptor.ts` tests: outgoing API requests carry
      `withCredentials: true` and no `Authorization` header sourced from storage; the 401→refresh retry flow
      calls `/auth/refresh` with `withCredentials` and no body token.
      Implemented as `auth.interceptor.spec.ts` (new file, 3 tests) using `provideHttpClient(withInterceptors(...))`
      + `provideHttpClientTesting()`. Confirmed RED (withCredentials false, refresh never called) before 4.17,
      GREEN after.
- [x] 4.17 Update `auth.interceptor.ts`: drop `attachBearerToken`; set `withCredentials: true` on
      `isApiRequest` URLs; keep the 401→refresh retry logic but let the cookie carry the refresh token.
      Done via a shared `withCredentialsRequest()` helper applied both to the initial request and the
      post-refresh retry.
- [x] 4.18 **[test-first]** Add/update tests for `ensureAuthenticated`: it calls `/auth/me`; on 401 it tries
      refresh then retries `/auth/me`; otherwise redirects to login. Remove any test asserting client-side
      `isTokenExpired` JWT parsing. Covered by `auth.service.spec.ts`'s three `ensureAuthenticated` tests
      (direct success, refresh-then-retry success, both-fail redirect). No prior `isTokenExpired` test existed
      to remove (grep-confirmed no spec file referenced it before this change).
- [x] 4.19 Remove `isTokenExpired` / client-side JWT parsing from `auth.service.ts`; rework
      `ensureAuthenticated` to the `/auth/me`-driven flow described in 4.18. Follow the angular-core skill:
      signals + `inject()`, no lifecycle hooks; RxJS is acceptable here for the refresh-retry flow per the
      skill's "complex async" allowance.
      Done. Also updated `logout()` to `POST /api/auth/logout` (revokes the server-side refresh token +
      clears cookies) before clearing local state — necessary so 4.20's full round-trip (including logout)
      actually invalidates the session server-side, not just client-side.
- [x] 4.20 End-to-end manual/integration check: login → protected call with no stored token → refresh on 401 →
      logout → protected call now rejected. Confirms the full cookie round-trip works across both halves in
      the same PR (no drift window).
      No live browser/e2e environment available in this apply batch (per scope boundary, consistent with
      SEC-01's operator-executed runbook steps). Strongest verification performed instead: the full round-trip
      is covered end-to-end by automated tests spanning both halves —
      `AuthCookieIssuanceTests`/`AuthCookieAcceptanceTests` (login → cookie → protected call),
      `AuthRefreshLogoutCookieTests` (refresh reissues cookies; logout revokes + clears + subsequent protected
      call rejected) on the backend, and `auth.service.spec.ts`'s `ensureAuthenticated` 401→refresh→retry test
      plus `auth.interceptor.spec.ts`'s 401→refresh→retry-original-request test on the frontend. Together these
      exercise every state transition in the manual script above except an actual real browser executing it.

**Estimated changed lines:** ~350–450 (backend cookie plumbing + refresh contract change + headers +
~6 backend tests, plus Angular service/interceptor rewrite + ~5 frontend tests). **Flag: likely exceeds 400.**
See Review Workload Forecast below — this slice is the one most likely to need `size:exception` or a
within-PR commit split (backend cookie+headers as commits 1–2, Angular cutover as commit 3) while still
shipping as a single PR per the "no dual-accept window" design decision.

---

## PR 5 / SEC-04 — TLS enforced on the ISO 8583 TCP channel

**Spec:** `security-hardening` SEC-10. **Depends on:** PR 2 merged. **Independent of:** SEC-02/03/05.

- [x] 5.1 **[test-first]** Add a test: binding `TcpIsoClientOptions` with no explicit `UseTls` value resolves
      `UseTls == true` — satisfying scenario "UseTls defaults to true when unspecified". Confirm it fails
      against the current `public bool UseTls { get; set; } = false;` default.
      Implemented as `TcpIsoClientOptionsTests` (2 tests: default construction + config-section bind with no
      explicit `UseTls` key). Confirmed RED (both failed, `Expected: True / Actual: False`) against the
      pre-5.2 `= false` default, GREEN after.
- [x] 5.2 Flip the default in `TcpIsoClientOptions.cs`: `public bool UseTls { get; set; } = true;`.
      Done — one-line change.
- [x] 5.3 **[test-first]** Add a test: `ASPNETCORE_ENVIRONMENT=Production` + non-loopback host (e.g.
      `acquirer.example.com`) + `UseTls=false` causes `IsoSwitch.Api` startup to throw before accepting
      traffic, with an error message referencing the ISO TCP TLS setting and the offending host, exiting
      non-zero — satisfying scenario "Production with TLS disabled for a non-loopback host fails startup".
      Implemented as `TcpIsoClientTlsStartupTests.Production_NonLoopbackHost_TlsDisabled_ThrowsOnStart`
      (`IsoSwitchWebApplicationFactory` + `WithWebHostBuilder` env/host/UseTls override, mirroring
      `StartupSecretValidationTests`). Confirmed RED before 5.6 (no exception thrown — request succeeded),
      GREEN after.
- [x] 5.4 **[test-first]** Add a test: same Production + `UseTls=false` but host is `127.0.0.1` (loopback) —
      startup succeeds — satisfying scenario "Production with TLS disabled for a loopback host is permitted".
      Implemented as `TcpIsoClientTlsStartupTests.Production_LoopbackHost_TlsDisabled_StartsSuccessfully`.
- [x] 5.5 **[test-first]** Add a test: `ASPNETCORE_ENVIRONMENT=Development` + `UseTls=false` for a
      non-loopback host — startup succeeds (fail-fast applies only in Production) — satisfying scenario
      "Development with TLS disabled for a non-loopback host is permitted".
      Implemented as `TcpIsoClientTlsStartupTests.Development_NonLoopbackHost_TlsDisabled_StartsSuccessfully`.
      Also added a sanity regression test, `Production_NonLoopbackHost_TlsEnabled_StartsSuccessfully`, proving
      the new `UseTls=true` default itself doesn't break Production startup.
- [x] 5.6 Implement the startup validation in IsoSwitch `Program.cs` (in the existing `IsoClient`-binding
      factory, or a dedicated `IValidateOptions<TcpIsoClientOptions>` mirroring the
      `TokenizationOptionsValidator`/`JwtOptionsValidator` fail-fast pattern already in the codebase): throw
      `InvalidOperationException` when `!env.IsDevelopment() && !opt.UseTls && !IsLoopback(opt.Host)`.
      Implemented `IsoSwitch.Api/Security/TcpIsoClientOptionsValidator.cs` as
      `IValidateOptions<TcpIsoClientOptions>` (constructor-injects `IHostEnvironment`), registered via
      `AddOptions<TcpIsoClientOptions>().BindConfiguration("IsoClient").ValidateOnStart()` +
      `AddSingleton<IValidateOptions<TcpIsoClientOptions>, TcpIsoClientOptionsValidator>()` in `Program.cs`,
      exactly mirroring the Tokenization/Jwt validator registration pattern. `ValidateOptionsResult.Fail(...)`
      is surfaced as `OptionsValidationException` by the framework's `ValidateOnStart` hosted-service pipeline
      (same mechanism already exercised by `StartupSecretValidationTests`), so no manual
      `InvalidOperationException` throw was needed — the framework's own exception type satisfies "throws
      before accepting traffic, exits non-zero, message references the setting and host".
      **Test-infra discovery (2 pre-existing gaps, fixed to make Production-environment testing possible at
      all):** (1) `IsoSwitch.Api`'s DB-migration bootstrap block called `Database.MigrateAsync()` whenever
      `!IsDevelopment()`, which throws `InvalidOperationException` ("Relational-specific methods...") against
      the test factory's InMemory-swapped `IsoSwitchDbContext` — fixed by adding a
      `db.Database.ProviderName?.Contains("InMemory", ...)` guard alongside `IsDevelopment()`, identical to the
      precedent already used in `IsoAudit.Api/Program.cs`. (2) The `/simulator/options` minimal-API endpoint
      (`GET (IsoSimulatorOptions opt) => ...`) failed ASP.NET Core's parameter-inference at Production startup
      with `"Body was inferred but the method does not allow inferred body parameters"`, because
      `IsoSimulatorOptions` was only registered in DI inside the `IsDevelopment()`-gated simulator block — fixed
      by hoisting the (side-effect-free) `AddSingleton(simOpt)` registration outside the `if`, so the options
      POCO is always resolvable; the simulator's actual hosted services stay Development-gated, unchanged.
      Neither fix touches `AllowInvalidCert` (ADR-7, see 5.9) or any other unrelated behavior — both were
      required only to make `ASPNETCORE_ENVIRONMENT=Production` boot at all under `WebApplicationFactory`,
      a path this codebase had apparently never exercised for IsoSwitch before this batch.
- [x] 5.7 Implement `IsLoopback(host)`: fast path `IPAddress.TryParse` + `IPAddress.IsLoopback`, fall back to
      `Dns.GetHostAddresses` for hostnames treating `"localhost"` as loopback, and **fail closed** (treat as
      non-loopback → TLS required) if DNS resolution fails.
      Implemented as `TcpIsoClientOptionsValidator.IsLoopback` (internal static helper): literal-IP fast path
      via `IPAddress.TryParse`/`IPAddress.IsLoopback`; explicit `"localhost"` string check; DNS fallback via
      `Dns.GetHostAddresses` requiring every resolved address to be loopback; any exception (including
      resolution failure) is caught and returns `false` (fail closed).
- [x] 5.8 **[test-first]** Add a test for the fail-closed DNS case: an unresolvable hostname in Production with
      `UseTls=false` is treated as non-loopback and fails startup (does not silently permit plaintext).
      Implemented as `TcpIsoClientTlsStartupTests.Production_UnresolvableHost_TlsDisabled_ThrowsOnStart`, using
      the RFC 2606 reserved `.invalid` TLD (`this-host-does-not-exist.invalid`) — guaranteed never to resolve,
      deterministic in CI (no dependency on a live negative-DNS response for a real-looking domain). Confirmed
      RED (no exception) before 5.6/5.7, GREEN after.
- [x] 5.9 Confirm no change to the existing `AllowInvalidCert` gate at the `// ADR-7: AllowInvalidCert is
      permitted only in Development` comment — this task is additive only, ADR-7 stays as-is.
      Confirmed: the `opt.AllowInvalidCert = builder.Environment.IsDevelopment() && opt.AllowInvalidCert;` line
      and its ADR-7 comment are byte-for-byte unchanged; the new `AddOptions<TcpIsoClientOptions>()` +
      validator registration were added immediately above that block, additive only.

**Estimated changed lines:** ~90–130 (one-line default flip, one validator/startup check, one loopback
helper, ~6 tests). Well within budget.

---

## PR 6 / SEC-05 — Remove default admin seed + hardcoded admin API key

**Spec:** `identity-and-access` "JWT-Based Authentication" (modified scenarios); `security-hardening` SEC-11.
**Depends on:** PR 2 merged. **Independent of:** SEC-02/03/04.

**Scope caveat (carry into PR description):** this slice adds **fail-fast validation** of the IsoSwitch admin
API key, mirroring `JwtOptionsValidator`'s existing pattern. It does **not** add actual request-time API-key
authentication — grep confirms `Admin:ApiKey` is currently read by nothing in code. If wiring a consumer
(middleware that checks incoming requests against the key) is desired, that is a separate, explicit follow-on
task — do not let this PR's fail-fast validation be read as "the admin API is now protected."

### CardVault: Development-only seed

- [ ] 6.1 **[test-first]** Add a test: with `ASPNETCORE_ENVIRONMENT != Development` (e.g. `Production`) and an
      empty identity store, `CardVault.Api` startup does NOT create any administrative user, and no
      `admin@demo.com`/`Admin1234!` credential exists afterward — satisfying scenario "Non-Development never
      auto-seeds a default admin". Confirm it fails against the current unconditional seed block.
- [ ] 6.2 **[test-first]** Add/confirm a test: with `ASPNETCORE_ENVIRONMENT == Development` and an empty
      identity store, startup still seeds the default operator roles and admin user for local testing —
      satisfying scenario "Development seeds default operator roles and admin user" (regression guard so 6.3
      doesn't break local dev).
- [ ] 6.3 In `Program.cs`, wrap the admin/seed-user creation block (currently reading `Seed:AdminEmail` /
      `Seed:AdminPassword` with `?? "admin@demo.com"` / `?? "Admin1234!"` fallbacks) in
      `app.Environment.IsDevelopment()`, matching the gate already used for the catalog seed. Remove the `??`
      fallbacks entirely — read directly from `app.Configuration["Seed:AdminEmail"]` /
      `["Seed:AdminPassword"]`; if Development and missing, fail loudly or skip with a warning (never fabricate
      a known admin). Also remove the `?? "OpenBanking123!"` fallback on `Seed:OpenBankingClientSecret`,
      applying the same non-Development gate.
- [ ] 6.4 Confirm role-seeding (`Admin`/`Operator`/`Auditor`) stays unconditional (roles are not secrets) — no
      change needed there, only the user-seeding-with-shared-password moves inside the Development gate.

### IsoSwitch: operator-supplied admin API key, fail-fast

- [ ] 6.5 **[test-first]** Add a test: `IsoSwitch.Api` startup with the admin API key absent from all config
      sources throws before accepting traffic and exits non-zero — satisfying scenario "Missing admin API key
      causes startup failure".
- [ ] 6.6 **[test-first]** Add a test: admin API key set to the literal `"dev-admin-key"` causes startup to
      throw with an error message referencing the admin API key configuration — satisfying scenario "DEV
      placeholder admin API key causes startup failure".
- [ ] 6.7 **[test-first]** Add a test: a non-placeholder operator-supplied admin API key allows `IsoSwitch.Api`
      to start successfully and reach healthy state — satisfying scenario "Valid operator-supplied admin API
      key allows startup".
- [ ] 6.8 Introduce `AdminApiKeyOptions` bound to the `Admin` config section with `ValidateOnStart()`, and
      `AdminApiKeyOptionsValidator : IValidateOptions<AdminApiKeyOptions>` mirroring `JwtOptionsValidator`'s
      structure exactly (same `Forbidden` array pattern), adding `"dev-admin-key"` to the forbidden literal set
      alongside any existing `DEV_ONLY`/`CHANGE_ME`/`placeholder` entries, and failing when the key is
      missing/empty/too short.
- [ ] 6.9 Register `AddSingleton<IValidateOptions<AdminApiKeyOptions>, AdminApiKeyOptionsValidator>()` in
      IsoSwitch `Program.cs`, matching the CardVault `JwtOptionsValidator` registration pattern.
- [ ] 6.10 **[test-first]** Add a config-scan test/assertion: `appsettings.Development.json` for IsoSwitch
      does not contain the literal `"dev-admin-key"` after the change — satisfying scenario "Committed config
      does not contain the dev-admin-key placeholder" (this overlaps with PR 2's SEC-9 config-purge test but
      is re-asserted here since PR 6 stacks after PR 2 and must not regress it).

**Estimated changed lines:** ~120–170 (CardVault seed-gate edit, IsoSwitch options/validator pair + DI
registration, ~7 tests). Within budget.

---

## Spec clarification follow-up (non-blocking, MEDIUM)

- [ ] X.1 File a follow-up spec clarification on `vault-and-pci`: the base spec's "PCI-Safe Audit Events"
      requirement is read by some as "exactly one event per batch," but the terminal (zero-remaining)
      re-encryption batch is a no-op that emits **no** `cardvault.reencrypt.batch` event by design (see PR 2,
      task 2.10). Clarify the wording so no future test encodes an impossible "always exactly one event per
      batch including the terminal no-op batch" assertion. Suggested precise wording: "exactly one
      `VaultReencryptionBatchCompleted` event per batch that migrates at least one record; a batch that
      migrates zero records (including the terminal batch) emits no event." This is a documentation-only spec
      correction, not a behavior change — safe to land any time, does not block PR 2–6.

---

## Review Workload Forecast

| PR / Slice | Est. changed lines | 400-line risk | Notes |
|---|---|---|---|
| PR 1 — SEC-06 | 60–90 | Low | Config + CI only, no app code |
| PR 2 — SEC-01 | 250–350 | Medium (borderline) | Must land atomically for the orphan-proof gate; if it grows, split by commit within the PR, not by PR |
| PR 3 — SEC-02 | 200–280 | Low | EF migration + PinService rewrite + tests, within budget |
| PR 4 — SEC-03 | 350–450 | **High** | Backend+frontend cross-surface, no dual-accept window by design — cannot split across PRs without reopening the "token model straddles two schemes" risk the design explicitly rejected |
| PR 5 — SEC-04 | 90–130 | Low | One-line default flip + validator + tests |
| PR 6 — SEC-05 | 120–170 | Low | Two independent small gates (CardVault seed, IsoSwitch validator) |

**Chained PRs recommended: Yes** (already the mandated delivery strategy — stacked-to-main, one PR per SEC
item, in the fixed order SEC-06 → SEC-01 → {SEC-02, SEC-03, SEC-04, SEC-05}).

**400-line budget risk: High for PR 4 (SEC-03) only.** SEC-03 is intentionally kept as one PR per design (no
dual-accept window — splitting backend/frontend across two PRs would reintroduce the exact "token model
straddles two schemes across a merge boundary" risk the design rejected). Recommended handling: structure PR
4's commits as backend-cookie-issuance → backend-refresh-logout-headers → frontend-cutover (per
work-unit-commits skill), keep the PR itself atomic, and request `size:exception` for this one slice rather
than force a structural split that would violate the design's cross-surface-atomicity decision.

**Decision needed before apply: Yes, but scoped to PR 4 only.** Every other slice fits the ≤400-line stacked
review budget on its own. The orchestrator should surface the PR 4 exception request when reaching that slice
in `sdd-apply`, per the cached `delivery_strategy` (ask-on-risk / auto-chain / single-pr / exception-ok).

**Ordering confirmation:** PR 1 (SEC-06) and PR 2 (SEC-01) are strictly sequential and must merge in that
order. PR 3, PR 4, PR 5, PR 6 (SEC-02/03/04/05) are mutually independent in design terms and could theoretically
be developed in parallel branches, but because delivery is **stacked-to-main** (not feature-branch-chain), they
still merge to `main` one at a time in the sequence presented above; there is no requirement they merge in
SEC-02→03→04→05 numeric order specifically, only that each merges after PR 2 and before the next chosen slice
begins its own PR.
