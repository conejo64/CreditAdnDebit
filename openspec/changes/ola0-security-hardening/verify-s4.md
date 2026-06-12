# Verification Report - Slice 4: CORS Allowlist

## Metadata
Change: ola0-security-hardening | Slice: S4 | Branch: feat/ola0-s4-cors-allowlist
Stacked on: 294e611 (S3 tip)
Commits: 38a0257 RED, e5f958e GREEN, 0922deb docs, f12bbb3 incident fix
Mode: Strict TDD | Date: 2026-06-12
Verdict: PASS WITH WARNINGS (1 CRITICAL, 1 WARNING, 1 SUGGESTION)

---

## Completeness
Tasks 4.1-4.4 all [x] in tasks.md. S5/S6 remain unchecked.

---

## Build and Test Execution

Build: PASS (0 errors, 15 pre-existing warnings)

Full suite: 644/644 passed, 0 failed, 0 skipped

| Project | Tests | Delta |
|---------|-------|-------|
| CardVault.Tests | 573/573 | +2 |
| IsoSwitch.Tests | 53/53 | +2 |
| IsoAudit.Tests | 18/18 | +2 |
| Total | 644/644 | +6 |

IsoAudit 18/18 confirms f12bbb3 restored all 4 JwtHardeningTests.
Coverage: Not available.

---

## TDD Compliance - 6/6 checks passed

TDD Cycle Evidence:
- 4.1 CorsAllowlistTests x3: RED=38a0257, GREEN=e5f958e, 2 cases each, 638 baseline, N/A new files
- 4.2 Program.cs: covered by 4.1 tests
- 4.3 appsettings: config-only
- 4.4 Verify: 644 green confirmed

RED Integrity Analysis:

Under AllowAnyOrigin, ASP.NET Core CORS sets Access-Control-Allow-Origin: * for every preflight.

EvilOrigin_NoCorsHeader_Returned: Assert.False(Contains(ACAO)) under AllowAnyOrigin Contains() returns true making Assert.False(true) fail. Genuinely RED.

AllowlistedOrigin_CorsHeader_Returned: Assert.Equal(specific-origin, wildcard) fails since AllowAnyOrigin echoes *, not the specific origin. Genuinely RED.

Bug fixed in e5f958e: original RED test called GetValues() inline in the format string. Under AllowAnyOrigin the header IS present so no throw at RED time. But once GREEN (evil origin gets no header), format string would throw InvalidOperationException. Fix caches hasAcaoHeader first. Correct fix; did not affect RED authenticity.

Conclusion: RED tests were NOT vacuously green.

Test Layer Distribution:
- Integration: 6 tests / 3 files / xUnit + WebApplicationFactory
- Unit: 0 | E2E: 0 | Total: 6

---

## Spec Compliance Matrix (SEC-6, ADR-4) - 5/5 compliant

SEC-6: Evil origin blocked x3 services - COMPLIANT
SEC-6: Allowlisted origin echoed x3 services - COMPLIANT
ADR-4: AllowAnyOrigin absent (grep 0 hits) - COMPLIANT
ADR-4: Origins from Cors:AllowedOrigins - COMPLIANT
ADR-4: AllowCredentials + WithOrigins (valid combo) - COMPLIANT

---

## Correctness (Static Evidence)

AllowAnyOrigin absent CardVault Program.cs line 52: PASS
AllowAnyOrigin absent IsoSwitch Program.cs line 49: PASS
AllowAnyOrigin absent IsoAudit Program.cs line 18: PASS
Grep AllowAnyOrigin across backend: 0 production hits. PASS
Cors:AllowedOrigins empty array in all 3 appsettings.json: PASS
CardVault appsettings.Development.json - localhost:4200: PASS
IsoAudit appsettings.Development.json - localhost:4200: PASS
IsoSwitch appsettings.Development.json - empty array: FAIL (see C-1)
Cors__AllowedOrigins__0 in docs/env.example: PASS
app.UseCors() preserved in all 3: PASS
AllowCredentials with WithOrigins (valid): PASS

---

## Incident Fix Verification (commit f12bbb3)

Incident: 4efbeac accidentally reverted IsoAudit.Api/Program.cs to pre-S2 state.
Fix: f12bbb3 reverse-applied the stray hunk.

IsoAudit.Api/Program.cs HEAD contains BOTH:
- ADR-4 CORS (lines 16-19): WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
- ADR-2 JWT hardening (lines 28-49): ValidateIssuer/Audience=true, ValidateLifetime=true, RequireHttpsMetadata=!IsDevelopment(), IOptions PostConfigure

All 4 JwtHardeningTests pass within 18/18 IsoAudit total:
- WrongIssuer_Returns401: PASS
- WrongAudience_Returns401: PASS
- DevelopmentEnv_RequireHttpsMetadata_IsFalse: PASS
- ProductionEnv_RequireHttpsMetadata_IsTrue: PASS

InMemory guard at Program.cs line 77 enables ProductionEnv test.
Incident fix confirmed.

---

## Design Coherence

ADR-4 named allowlist from config: Yes - pattern identical across all 3 services
ADR-4 AllowCredentials with WithOrigins: Yes - valid; old AllowAnyOrigin+credentials would throw at runtime
Default empty array (fail-secure): Yes - no CORS in prod until provisioned
Dev origins in appsettings.Development.json: Partial - IsoSwitch empty (see C-1)
env.example documents provisioning: Yes - Cors__AllowedOrigins__0 present

---

## Assertion Quality - 0 CRITICAL, 0 WARNING

3 test files scanned (CorsAllowlistTests.cs x3 services).
- No tautologies found.
- Both tests exercise real HTTP preflight through WebApplicationFactory.
- EvilOrigin: Assert.False(hasAcaoHeader) verifies real runtime CORS policy behavior.
- AllowlistedOrigin: dual assertion - Contains check + Assert.Equal(exact origin value).
- hasAcaoHeader intermediate variable eliminates GetValues exception risk for passing tests.
- No ghost loops, no smoke-test-only assertions, no type-only assertions.

---

## Scope Discipline (S5/S6 Regression Check)

AuthController.cs: no changes in S4 range. PASS
BillingService.cs: no changes in S4 range. PASS
SwitchTxnConsumer.cs: no changes in S4 range. PASS
tasks.md 0922deb: only S4 checked off; S5/S6 still unchecked. PASS

---

## Issues Found

### CRITICAL

C-1: IsoSwitch appsettings.Development.json empty CORS allowlist breaks Angular dev frontend

Evidence:
- frontend/src/environments/environment.ts line 4: isoSwitchUrl = http://localhost:5201/api
- frontend/src/app/features/switch/switch.service.ts: getTransactions, simulateAuthorize, simulateReversal, simulateCapture all use isoSwitchUrl
- frontend/src/app/features/dashboard/dashboard.service.ts line 30: getMetrics uses isoSwitchUrl/transactions

Impact: Angular dev (localhost:4200) gets CORS failures for all IsoSwitch endpoints. Simulator and dashboard features non-functional in development.

Current state: IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json has AllowedOrigins: []

Fix: Add http://localhost:4200 to IsoSwitch appsettings.Development.json, matching CardVault and IsoAudit entries.

### WARNINGS

W-1: apply-progress shows IsoAudit 15/18 (pre-f12bbb3 snapshot). Stale. Actual is 18/18. Code correct.

### SUGGESTIONS

S-1: AllowCredentials() constraint should be noted in inline comments. Any future AllowAnyOrigin addition would throw at runtime.

---

## Git Hygiene

git status: clean. Nothing staged. No working tree mutations.

---

## Verdict

PASS WITH WARNINGS (1 CRITICAL, 1 WARNING, 1 SUGGESTION)

C-1 is a developer-experience regression, not a security regression. Production CORS is correctly empty. Angular dev frontend cannot reach local IsoSwitch because localhost:4200 is missing from IsoSwitch Development config.

Fix C-1 before starting S5.

S4 implementation correct. All 644 tests pass. AllowAnyOrigin eliminated across backend. Incident fix (f12bbb3) confirmed. JWT hardening and CORS coexist correctly in IsoAudit Program.cs. TDD evidence is genuine.