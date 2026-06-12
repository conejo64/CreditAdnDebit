# Verify Report — Slice 1: Startup Secret Validation
## Change: ola0-security-hardening
## Slice: S1 (SEC-1, SEC-2, SEC-3)
## Branch: feat/ola0-s1-startup-secret-validation
## Reviewed commits: 128b64c..HEAD
## Date: 2026-06-11
## Verdict: PASS WITH WARNINGS

---

## Test Suite Result

| Project | Tests | Result |
|---------|-------|--------|
| IsoAudit.Tests | 12 | PASS |
| IsoSwitch.Tests | 49 | PASS |
| CardVault.Tests | 571 | PASS |
| Total | 632 | GREEN |

Expected 632 (baseline 596 + 36 new). Build: 0 errors.

---

## Findings

### WARNING W-1: appsettings.Development.json files still contain DEV placeholders

Spec ref: Task 1.8 — remove placeholder from both appsettings.json AND appsettings.Development.json.

appsettings.json was cleaned (set to empty string). appsettings.Development.json was NOT touched and is tracked in git:

- backend/services/CardVault/src/CardVault.Api/appsettings.Development.json:20 — SigningKey DEV_ONLY_change_me_please_32+chars
- backend/services/IsoAudit/src/IsoAudit.Api/appsettings.Development.json:13 — Key DEV_ONLY_change_me_please_32+chars
- backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json:8 — SigningKey DEV_ONLY_change_me_please_32+chars

Because appsettings.Development.json overrides appsettings.json at runtime, a developer who clones the repo and runs dotnet run in Development mode will hit OptionsValidationException pointing at DEV_ONLY strings they never configured. The fail-fast behavior is correct but the UX is confusing. Task 1.8 explicitly required this cleanup.

Recommended fix: set the secret keys to empty string in all three appsettings.Development.json files, matching the appsettings.json treatment.

Severity: WARNING — security invariant holds (validator rejects DEV_ONLY regardless of source), but violates Task 1.8 spec and causes confusing startup errors.

---

### WARNING W-2: IsoAudit JWT bearer reads key via raw IConfiguration, not IOptions

File: backend/services/IsoAudit/src/IsoAudit.Api/Program.cs:24-25

The comment says Key resolved from strongly-typed IOptions but the code reads builder.Configuration[Jwt:Key] directly. This is functionally correct (same config source, ValidateOnStart blocks the host before any traffic), but the comment is misleading and the architecture diverges from ADR-1. If ValidateOnStart were ever disabled, the bearer would use a zero-byte key silently.

Recommended fix: move IssuerSigningKey assignment to use IOptions<JwtOptions>.Value.Key read after app.Build().

Severity: WARNING — low practical risk given ValidateOnStart; ADR-1 compliance issue.

---

### SUGGESTION S-1: IsoAudit JwtOptionsValidator is internal vs public

CardVault and IsoSwitch validators are public sealed. IsoAudit uses internal sealed with InternalsVisibleTo(IsoAudit.Tests). InternalsVisibleTo is the more correct pattern (no unnecessary public API surface). CardVault/IsoSwitch could be aligned to internal + InternalsVisibleTo in a cleanup pass.

No action required this slice.

---

### SUGGESTION S-2: IsoSwitch Jwt:SigningKey has no startup validator

By design (SEC-1 only covers Tokenization:Secret). However appsettings.json sets Jwt:SigningKey to empty string, so a deployment without that env var silently uses a zero-byte JWT signing key. Note for future hardening.

---

## Verification Matrix: SEC-1 / SEC-2 / SEC-3

| Requirement | Validator exists | ValidateOnStart | All forbidden cases |
|-------------|-----------------|-----------------|---------------------|
| SEC-1 IsoSwitch Tokenization:Secret | YES | YES | YES |
| SEC-2 CardVault Jwt:SigningKey | YES | YES | YES |
| SEC-3 IsoAudit Jwt:Key | YES | YES | YES |

All validators reject: empty, whitespace, length < 32, DEV_ONLY, CHANGE_ME, change_me, placeholder (case-insensitive).

---

## Migration Guard

IsoSwitch and IsoAudit now mirror CardVault convention:
  if (IsDevelopment) EnsureCreatedAsync() else MigrateAsync()

No production regression. InMemory test contexts use EnsureCreated path correctly.

---

## Scope Discipline

CORS AllowAnyOrigin: untouched (S4). /register [AllowAnonymous]: untouched (S5). TcpIsoClient Console.WriteLine: untouched (S3). Statement dedup: untouched (S6). IsoAudit issuer/audience ValidateIssuer=false: unchanged (S2). No scope bleed.

---

## Test Factory Keys

All test secrets are valid (>=32 chars, no forbidden substrings):
- CardVaultWebApplicationFactory.SigningKey = TestSigningKeyForCardVaultIntegrationTests (42 chars)
- IsoSwitchWebApplicationFactory.TestTokenizationSecret = TestTokenizationSecretForIsoSwitch32Plus (40 chars)
- IsoSwitchWebApplicationFactory.TestJwtSigningKey = TestJwtSigningKeyForIsoSwitchTests32Plus (40 chars)
- IsoAuditWebApplicationFactory.TestJwtKey = TestJwtKeyForIsoAuditServiceTests32Plus (39 chars)

CardVaultWebApplicationFactory injects Jwt:SigningKey via UseSetting. TokenService injects IOptions<JwtOptions> and reads _opt.SigningKey — same value. JWT minting and existing integration tests unaffected.

---

## Task 1 Completion

| Task | Status |
|------|--------|
| 1.1 env.example + secrets.md | COMPLETE |
| 1.2 Host-refuses-to-start tests | COMPLETE (9 tests) |
| 1.3 Validator unit matrix | COMPLETE (24 tests) |
| 1.4 TokenizationOptions + IsoAudit JwtOptions | COMPLETE |
| 1.5 IValidateOptions validators | COMPLETE |
| 1.6 AddOptions().ValidateOnStart() | COMPLETE |
| 1.7 TokenPanService to IOptions | COMPLETE |
| 1.8 Clean appsettings.json placeholders | PARTIAL — appsettings.json done, appsettings.Development.json skipped (W-1) |
| 1.9 GREEN 632 tests | COMPLETE |

---

## Verdict

PASS WITH WARNINGS — 0 CRITICAL, 2 WARNINGS, 2 SUGGESTIONS.

W-1 should be fixed before merging to main (3-line change across appsettings.Development.json files).
Next recommended: fix W-1 in a follow-up commit, then sdd-archive S1, then Slice 2.
