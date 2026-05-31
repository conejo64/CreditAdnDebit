# Verify Report — fix-vault-rotation-policy (Re-verify Pass)
**Date:** 2026-05-30
**Mode:** hybrid (Engram unavailable — file-only)
**Verdict:** PASS
**Test run:** CardVault.Tests: 288 passed (+4 vs prior 284), 0 failed, 0 skipped

---

## Re-Verify Scope

Adversarial re-verification of W-1 atomicity, S-1 startup guard, W-3 traceId integration assertion, and W-2 T-02 task checkbox. Full test suite re-run with real pass/fail counts.

---

## Test Results

CardVault.Tests: **288 passed, 0 failed, 0 skipped** (duration: 13 s).
Delta vs prior verify (284): **+4 tests** — added `TokenVaultServiceRotateColdStartAtomicityTests` (4 new facts).
No tests deleted or skipped.

Cold-start atomicity subset (filter `TokenVaultServiceRotateColdStart`): 4/4 passed.
Startup guard subset (filter `VaultRateLimitPolicyRegistrationTests`): 4/4 passed.

---

## Finding Results

### W-1: Atomicity — RESOLVED (was WARNING, now CLEAR)

**Implementation verified in `TokenVaultService.cs:111-148`:**

1. `GetAsync(ct)` called first (line 121) — resolves or creates the VaultSettings singleton; on cold-start, this triggers the internal `SaveChangesAsync` inside `VaultSettingsStore.GetAsync` to create the row.
2. Entity mutated directly (`s.ActiveKeyId`, `s.UpdatedOn`, `s.LastReencryptStatus`) on the tracked entity — no separate save.
3. Outbox row staged AFTER `GetAsync` returns (line 129-141) — so the outbox row is NOT present in the cold-start VaultSettings-creation save.
4. Single `_db.SaveChangesAsync(ct)` at line 144 commits both the key mutation and the outbox row atomically.

**Side-effect audit:** `SetActiveKeyIdAsync` is no longer called. The old path set `ActiveKeyId`, `UpdatedOn`, and `LastReencryptStatus = "rotated"`. All three fields are now set directly at lines 124-126 in `RotateActiveKeyAsync`. No side effect dropped.

**Cold-start test quality (`TokenVaultServiceRotateColdStartAtomicityTests.cs:87-112`):**
- Uses a `SaveChangesInterceptorSpy` (EF Core `SaveChangesInterceptor`) that records entity type names at EACH `SaveChangesAsync` call.
- Asserts: first save snapshot does NOT contain `OutboxMessageEntity`.
- Asserts: last save snapshot DOES contain `OutboxMessageEntity`.
- This test would FAIL on the old ordering (where outbox was added before `GetAsync`): on the old code, the outbox row would appear in `spy.SaveCallSnapshots[0]`, triggering the `NotContain("OutboxMessageEntity")` assertion failure.
- Additional facts: key and outbox row both visible from fresh context, warm-start variant also passes.

**W-1 verdict: RESOLVED.**

### S-1: Startup Guard — RESOLVED (was SUGGESTION, now CLEAR)

**Implementation verified in `Program.cs:215-241`:**

Field name changed from `_policyMap` to `<PolicyMap>k__BackingField` — the correct compiler-generated backing field name for an auto-property named `PolicyMap` in C# / .NET 9.

Explicit null-check at lines 218-222:
```
if (policyMapField == null)
    throw new InvalidOperationException("Startup assertion failed: could not locate ...")
```

The guard is NOT dead code: `VaultRateLimitPolicyRegistrationTests.ApplicationHost_BootsWithBothPoliciesRegistered` boots the full ASP.NET Core test host via `CardVaultWebApplicationFactory`. This test passed (verified at runtime), which means the reflection field resolved non-null and the assertion ran successfully against the real `RateLimiterOptions` instance in .NET 9. If the field name were wrong, the guard would throw at startup and the test would fail.

The remaining fragility (field name tied to ASP.NET internals) is documented in code comments (lines 210-214) with an explicit message telling operators what to fix if it breaks in a future .NET version. This is appropriate defensive practice.

**S-1 verdict: RESOLVED — guard is live and verified at runtime.**

### W-2: T-02 Task Checkbox — RESOLVED (was WARNING, now CLEAR)

`tasks.md` line 25: `- [x] **IMPLEMENT (no test needed...)**` — checkbox is now ticked.
All 14 tasks are marked complete (`[x]`).

### W-3: Integration Test traceId Assertion — RESOLVED (was WARNING, now CLEAR)

`VaultRotateAuditIntegrationTests.cs:89-92`:
```csharp
payload.TryGetProperty("traceId", out var traceIdProp).Should().BeTrue(
    because: "the spec requires traceId in the VaultKeyRotated outbox payload");
traceIdProp.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined, ...);
```

The test now asserts `traceId` is present in the integration-path audit payload. The value comes from `HttpContext.TraceIdentifier` (controller line 76), which is always a non-empty string in ASP.NET Core — never null.

Residual note: the assertion checks presence and non-Undefined value, but does not assert `NotBeNullOrEmpty()` on the string value. This is technically weaker, but `TraceIdentifier` is guaranteed non-null by the framework. Flagged as a SUGGESTION only.

---

## CRITICAL Findings

None.

---

## WARNING Findings

None. All three prior warnings (W-1, W-2, W-3) are resolved.

---

## SUGGESTION Findings

### S-1 (residual): traceId assertion could be stronger

**File:** `VaultRotateAuditIntegrationTests.cs:89-92`

The integration test asserts `traceId` property exists and is not Undefined, but does not call `.GetString().Should().NotBeNullOrEmpty()`. In practice, `HttpContext.TraceIdentifier` is always non-empty, so this is low risk. Suggest tightening the assertion in a follow-up.

### S-2 (carry-over): Empty re-encrypt batch emits no audit row — not documented in runbook

**File:** `TokenVaultService.cs:182`

`if (updated > 0)` gates outbox emission. Correct per spec. The runbook (`vault-key-rotation.md`) should document that a no-op reencrypt produces no audit row so operators are not alarmed by its absence.

---

## Spec Scenario Coverage Matrix

| Scenario | Status |
|----------|--------|
| Authorized admin 200 within rate window | COVERED |
| Rotation throttled 429 | COVERED |
| Reencrypt throttled 429 | COVERED |
| Unauthorized 403 on rotate | COVERED |
| Unauthorized 403 on reencrypt | COVERED |
| Startup asserts both policies registered | COVERED + LIVE (runtime-verified) |
| Active key changes persist across restarts | ARCHITECTURE VERIFIED |
| Background re-encryption in batches | COVERED |
| Rotate emits exactly one VaultKeyRotated outbox row | COVERED |
| VaultKeyRotated payload: actor, keyId, rotatedAt | COVERED (unit + integration) |
| VaultKeyRotated payload: traceId | COVERED (unit + integration) |
| Re-encrypt emits VaultReencryptionBatchCompleted | COVERED |
| recordsAffected correct count | COVERED |
| Throttled request no audit row | COVERED |
| 403 request no audit row | COVERED |
| Audit survives event-bus outage via outbox | ARCHITECTURE VERIFIED |
| Cold-start atomicity: outbox not in cold-start save | COVERED (new - 4 facts) |
| Warm-start atomicity: single SaveChangesAsync | COVERED |

---

## Task Completeness

14/14 checked. All work units (WU1–WU6) complete.

---

## Final Verdict

**PASS**

- 0 CRITICAL findings
- 0 WARNING findings (all 3 prior warnings resolved)
- 2 SUGGESTIONS (traceId assertion strength; runbook no-op note)

288 CardVault tests pass (+4 from prior run). No tests deleted or skipped. Cold-start atomicity is genuinely proven by a SaveChangesInterceptor spy that would fail on the old ordering. Startup guard is live and runtime-verified.

**Archive ready: YES**

**Next recommended: sdd-archive**
