# Verify Report: Phase 0 - Security Blockers

Change: phase0-security-blockers
Verified against: main @ 4a67dd6 (all 6 SEC-0x PRs merged: PR #8-#13)
Date: 2026-07-24
Verifier: sdd-verify (fresh code + test audit against spec/design/tasks)

## Summary

All 6 SEC slices (SEC-01..SEC-06) are genuinely implemented on main, not just checked off. Every spec
behavioral scenario reviewed has a corresponding, correctly-behaving implementation. Backend test suites pass
in full (705/705 across the 3 test projects, run in isolation to avoid a known environmental flake). No
CRITICAL findings. One pre-existing, explicitly-flagged non-blocking WARNING (task X.1, doc-only spec wording
clarification) remains open by design. A handful of SUGGESTIONs for follow-up hardening are noted below.

## Test Execution Evidence

Build: dotnet build backend/CardSwitchPlatform.sln -c Release - succeeded (0 errors, pre-existing warnings
only: NU1902 OpenTelemetry advisory, SYSLIB0053 AesGcm obsolete ctor, nullability warnings - all pre-existing,
none introduced by this change).

Per-project isolated dotnet test runs (isolated deliberately, per the known WebApplicationFactory +
ValidateOnStart disposal-race flake documented in apply-progress and reproduced by this session's launch
instructions):

| Project | Result |
|---|---|
| CardVault.Tests | 624/624 passed |
| IsoSwitch.Tests | 63/63 passed |
| IsoAudit.Tests | 18/18 passed |
| Total | 705/705 passed |

These counts match the apply-progress notes exactly (624 CardVault incl. 2 SEC-05 tests, 63 IsoSwitch incl. 3
SEC-05 tests, 18 IsoAudit unchanged). No flake was hit in this isolated-run verification; the flaky class
(ObjectDisposedException racing OptionsValidationException under full-solution parallel load) is a known,
previously-documented environmental issue in AdminApiKeyStartupTests / IsoAudit_ShortJwtKey_ThrowsOnStart /
JwtOptionsValidator tests and was avoided by running each test project in isolation rather than the full
solution - this is the correct way to distinguish the flake from a real regression, and no real regression was
found.

## Spec-to-Code Verification (spot-checked every ADDED/MODIFIED requirement)

| Requirement | Spec file | Code verified | Result |
|---|---|---|---|
| SEC-9 (no secret material in committed config) | security-hardening | CardVault.Api/appsettings.Development.json - Vault.Keys empty, Seed.* empty, ConnectionStrings.* empty; IsoSwitch.Api/appsettings.Development.json - Admin.ApiKey empty; backend/deploy/.env.example documents required vars with placeholders | PASS |
| SEC-10 (ISO TCP TLS default-on + Production fail-fast) | security-hardening | TcpIsoClientOptions.cs:8 - UseTls defaults to true; TcpIsoClientOptionsValidator.cs registered via ValidateOnStart() | PASS |
| SEC-11 (IsoSwitch admin API key fail-fast, rejects dev-admin-key) | security-hardening | AdminApiKeyOptionsValidator.cs - Forbidden array includes "dev-admin-key", min-length 32 enforced | PASS |
| SEC-12 (CardVault response security headers) | security-hardening | SecurityHeadersMiddleware.cs - sets X-Content-Type-Options: nosniff, X-Frame-Options: DENY, Content-Security-Policy | PASS |
| Cookie-Based Token Delivery (SEC-03) | identity-and-access | AuthCookieWriter.cs - HttpOnly = true, Secure = true unconditional (no env branch exists), SameSite = Lax | PASS |
| Non-Development never auto-seeds default admin (SEC-05) | identity-and-access | CardVault.Api/Program.cs - user-seed block wrapped in IsDevelopment() check, no fallback default values, role-seed stays unconditional | PASS |
| Re-Encryption Under Rotated Key + Old-Key Revocation (SEC-01) | vault-and-pci | Reuses pre-existing TokenVaultService.RotateActiveKeyAsync / ReEncryptBatchAsync; orphan-proof COUNT gate tests (VaultRevocationGateTests) present | PASS |
| Salted, Cost-Parameterized PIN Hashing (SEC-02) | vault-and-pci | PinService.cs - Argon2id class used, PinHashAlgorithm/PinSalt/PinHashParams columns, VerifyLegacySha256 legacy branch + upgrade-on-verify | PASS |
| CICD-13 (secret-scan CI job) | cicd-packaging | .github/workflows/ci.yml - secret-scan job using gitleaks/gitleaks-action@v2, GITLEAKS_CONFIG: .gitleaks.toml | PASS |
| CICD-14 (pre-commit hook) | cicd-packaging | .pre-commit-config.yaml present at repo root | PASS |

Also confirmed: .gitleaks.toml contains the two custom rules (inline-connection-string-password,
vault-base64-key-material) added during PR 1 verification to close a real gitleaks built-in-ruleset gap for
this codebase's specific leaked-secret shapes; backend/SECRETS-ROTATION-RUNBOOK.md exists with the 9-step
ordered runbook; CommittedConfigSecretShapeTests.cs exists and is part of the passing CardVault.Tests suite.

## Tasks-to-Code Verification

All checked-off items in tasks.md for PR 1-6 (SEC-06, SEC-01, SEC-02, SEC-03, SEC-04, SEC-05) were spot-checked
against real code, not merely trusted from the checkbox. Every completed task's claimed artifact (file, class,
test) was found to exist and behave as described. The runbook-documented/operator-executed items in PR 2
(2.6, 2.8, 2.11, 2.13, 2.15) are honestly and correctly left un-checked - they are genuine out-of-band
operational actions (key rotation against a live environment, git history scrub) that are explicitly outside
sdd-apply's scope and are documented in the runbook rather than faked as complete. This is the correct
treatment, not a gap.

The one remaining unchecked task, X.1 (non-blocking spec clarification on vault-and-pci "exactly one event
per batch" wording), is correctly left open - it is documentation-only, does not block any PR, and does not
represent unfinished behavioral work.

## Findings

### CRITICAL
None.

### WARNING
1. (Pre-existing, non-blocking) Task X.1 - vault-and-pci spec wording ambiguity not yet corrected. The
   base spec's "PCI-Safe Audit Events" requirement can be misread as "exactly one event per batch including the
   terminal no-op batch," which is actually impossible by design (the terminal zero-remaining batch emits no
   event). This is flagged as non-blocking in tasks.md and does not affect any shipped behavior - the actual
   code and its tests (VaultRevocationGateTests) already implement and verify the correct behavior. Recommend
   filing this as a small follow-up spec-wording PR before or shortly after archive, since it is a two-line
   spec-doc edit with no code change.

### SUGGESTION
1. IsoSwitch Admin:ApiKey has no request-time consumer yet. This is explicitly and correctly flagged as a
   scope caveat in both the design and PR 6's task notes - SEC-05 delivers fail-fast validation only. If admin
   API endpoints exist or are planned, wiring actual request-time authentication against this key is a natural
   follow-on (Phase 1 candidate), not a Phase 0 gap.
2. SEC-03 loopback/dev topology assumption. The chosen SameSite=Lax design decision assumes SPA and API
   share a registrable domain in production (documented as an "assumption requiring validation" in design.md).
   Recommend confirming actual production deployment topology before or during the archive phase closes this
   out, since a split-domain deployment would require revisiting to SameSite=None; Secure.
3. PR 6 (SEC-05) branch feat/sec-05-remove-default-admin-seed is confirmed merged (visible directly in
   git log as commit 4a67dd6, PR #13) - consistent with the change now being fully code-complete on main.
   No further merge action needed for this verify pass.
4. Local build environment note (non-blocking, tooling only): the first dotnet build -c Release
   parallel-node attempt in this verification session hit an OutOfMemoryException inside MSBuild's
   conflict-resolution task, resolved by shutting down stale MSBuild/VBCSCompiler build-server processes and
   rebuilding with -m:1. This is an environment/tooling artifact of this verification session (stale build
   servers), not a code or CI issue - CI runs in a clean container per job and does not carry this state.

## Verdict

PASS - ready for sdd-archive. 0 CRITICAL, 1 WARNING (pre-existing, non-blocking, already tracked as
task X.1), 4 SUGGESTION. All 6 PR slices are code-complete, merged to main, and their behavioral contracts are
verified by passing tests and direct code inspection.
