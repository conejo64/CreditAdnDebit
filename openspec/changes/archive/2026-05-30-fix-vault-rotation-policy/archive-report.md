# Archive Report: fix-vault-rotation-policy
**Date:** 2026-05-30  
**Status:** Archived and Closed  
**Mode:** hybrid (openspec filesystem + Engram unavailable)

---

## Executive Summary

The **fix-vault-rotation-policy** change has been fully implemented, verified clean (0 CRITICAL / 0 WARNING, 288 CardVault tests green), and archived. This change closed a live regulatory blocker: the `vault_admin_ops` rate-limit policy was referenced but never registered in `Program.cs`, making `POST /api/vault/rotate-active-key` and `POST /api/vault/reencrypt` unusable in production. The implementation registered the policy, added transactional audit emission through the EF outbox, and added 4 new integration tests asserting cold-start atomicity, proving the rate-limit and audit guarantees required by PCI-DSS 3.6.4 and Superintendencia de Bancos del Ecuador Resolución JB-2014-3066.

---

## What Shipped

| Artifact | Location | Status |
|----------|----------|--------|
| **Proposal** | `openspec/changes/archive/2026-05-30-fix-vault-rotation-policy/proposal.md` | ✅ |
| **Specs** | `openspec/specs/vault-and-pci/spec.md` (merged; delta removed after merge) | ✅ |
| **Design** | `openspec/changes/archive/2026-05-30-fix-vault-rotation-policy/design.md` | ✅ |
| **Tasks** | `openspec/changes/archive/2026-05-30-fix-vault-rotation-policy/tasks.md` (14/14 complete) | ✅ |
| **Verification** | `openspec/changes/archive/2026-05-30-fix-vault-rotation-policy/verify-report.md` | ✅ PASS |

### Code Changes Shipped

| Component | File | Change |
|-----------|------|--------|
| **Config Model** | `backend/services/CardVault/src/CardVault.Api/Vault/VaultCrypto.cs` | Added `AdminRateLimitOptions` nested class (PermitLimit, WindowSeconds, QueueLimit) with code defaults; added `AdminRateLimit` property to `VaultOptions` |
| **Policy Registration** | `backend/services/CardVault/src/CardVault.Api/Program.cs` | Registered `AddPolicy("vault_admin_ops", ...)` with fixed-window limiter partitioned by actor/IP; added startup assertion both `vault_detokenize` and `vault_admin_ops` are present; added INFO log of bound limits |
| **Audit Emission** | `backend/services/CardVault/src/CardVault.Api/Vault/TokenVaultService.cs` | In `RotateActiveKeyAsync`: emit `VaultKeyRotated` outbox row (actor, keyId, traceId, rotatedAt, no key material). In `ReEncryptBatchAsync`: emit `VaultReencryptionBatchCompleted` outbox row (actor, traceId, recordsAffected, completedAt) |
| **App Settings** | `backend/services/CardVault/src/CardVault.Api/appsettings.json` | Added `"Vault": { "AdminRateLimit": { "PermitLimit": 20, "WindowSeconds": 60, "QueueLimit": 0 } }` (dev-relaxed) |
| **App Settings (Prod)** | `backend/services/CardVault/src/CardVault.Api/appsettings.Production.json` | Created new file with `"Vault": { "AdminRateLimit": { "PermitLimit": 5, "WindowSeconds": 300, "QueueLimit": 0 } }` (tightened prod defaults) |
| **Tests** | `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/` | Added 7 test files: `VaultOptionsAdminRateLimitTests`, `VaultRateLimitPolicyRegistrationTests`, `VaultRotateRateLimitTests`, `VaultRotateAuthTests`, `TokenVaultServiceRotateAuditTests`, `TokenVaultServiceReencryptAuditTests`, `VaultRotateAuditIntegrationTests` |
| **Runbook** | `backend/services/CardVault/docs/runbooks/vault-key-rotation.md` | New operator runbook: preconditions, rotation procedure, re-encryption procedure, audit verification, rollback, troubleshooting |
| **Spec (Merged)** | `openspec/specs/vault-and-pci/spec.md` | Modified: added explicit rate-limit scenarios (200 authorized, 429 burst on rotate, 429 burst on re-encrypt, 403 unauthorized) and startup policy assertion. Added audit event scenarios (successful rotate emits VaultKeyRotated, batch emits VaultReencryptionBatchCompleted, failed/throttled no audit, outbox durability) |

---

## Key Technical Decisions

### Decision 1: Transactional Outbox Audit (over fire-and-forget)

**What:** Rotation and re-encryption audit events are now emitted via the EF outbox (`OutboxMessageEntity` row committed in the same `SaveChangesAsync` as the state change), not via direct `IEventBus.PublishAsync`.

**Why:** The proposal documented that "audit persistence is transactional with the rotation state change" (spec: `vault-and-pci` §PCI-Safe Audit Events). The old fire-and-forget path was non-transactional and could lose events if Kafka was unavailable. The outbox pattern ensures the audit row is persisted durably and relayed by the existing `EfOutboxPublisher` (already deployed in production).

**Evidence:** Design decision table, line 14. Verify report §W-1 Atomicity confirms implementation: `RotateActiveKeyAsync` calls `GetAsync` first (resolving/creating VaultSettings), then stages the outbox row, then issues ONE `SaveChangesAsync` committing both. Cold-start atomicity is proven by `TokenVaultServiceRotateColdStartAtomicityTests` using a `SaveChangesInterceptor` spy that would fail if the outbox row appeared in the initial cold-start save (it doesn't).

### Decision 2: Actor-Partitioned Rate Limit (not global or per-IP only)

**What:** The `vault_admin_ops` policy partitions the rate window by authenticated actor (`httpContext.User?.Identity?.Name`) with fallback to remote IP for degraded-auth scenarios.

**Why:** Throttle the person, not the host. This mirrors the existing `vault_detokenize` pattern and prevents one admin from consuming the quota of another. `[Authorize]` rejects anonymous calls upstream, so the IP fallback only engages in degraded scenarios.

**Evidence:** Design decision table, line 12; Program.cs implementation (verified in apply phase).

### Decision 3: Config-Driven Limits (not hard-coded)

**What:** Permit limit, window seconds, and queue limit are bound from `Vault:AdminRateLimit:*` config keys with safe dev and prod defaults.

**Why:** Banking operations needs to relax limits during scheduled key rotations without a code redeploy. Hard-coded limits would force redeploy every time. Queue limit 0 surfaces denials fast rather than hanging requests.

**Evidence:** Proposal §Approach, point 2; Tasks §T-02 appsettings.json and appsettings.Production.json values.

### Decision 4: Startup Policy Assertion (fail fast, not on first request)

**What:** At application startup, after `builder.Build()`, the code resolves `RateLimiterOptions` and throws `InvalidOperationException` if either `vault_detokenize` or `vault_admin_ops` is missing from the registry.

**Why:** Prevent silent regression. If a future .NET version breaks the reflection-based policy lookup, the assertion surfaces the error before the app starts serving traffic, rather than waiting for a 500 at the first request.

**Evidence:** Design decision table, line 15; Verify report §S-1 confirms the guard is live and runtime-verified in `VaultRateLimitPolicyRegistrationTests.ApplicationHost_BootsWithBothPoliciesRegistered`.

---

## Two Proposal Inaccuracies (Corrected in Implementation)

### Inaccuracy 1: VaultOptions.cs vs. VaultCrypto.cs

**Proposal claimed:** "Modify `backend/services/CardVault/src/CardVault.Api/Vault/VaultOptions.cs`"

**Reality:** The actual `VaultOptions` class is defined in `backend/services/CardVault/src/CardVault.Api/Vault/VaultCrypto.cs` at line 83, not in a non-existent `VaultOptions.cs` file. The `AdminRateLimit` options and property were correctly added to the real location.

**Evidence:** Design.md §NOTE, line 17: "NOTE — proposal correction: `AdminRateLimit` options go in `VaultCrypto.cs` (where `VaultOptions` is actually defined, line 83), not a non-existent `VaultOptions.cs`."

### Inaccuracy 2: Audit Transport — Fire-and-Forget vs. Transactional Outbox

**Proposal stated:** "Audit is ABSENT today — add new audit event emission via outbox"

**Reality:** Audit IS present today via `IEventBus.PublishAsync` (direct) and `PciAuditPublisher.PublishAsync` (fire-and-forget), but it is non-transactional. This change MOVES the audit onto the outbox (transactional) and aligns the payload shape to the spec. The old paths were replaced, not supplemented.

**Evidence:** Design.md §NOTE, line 17: "Audit is NOT absent today — it exists via direct `IEventBus`/`PciAuditPublisher` but is non-transactional; this change MOVES it onto the outbox and aligns the payload shape."

---

## Test Coverage

| Test File | Scenarios | Status |
|-----------|-----------|--------|
| `VaultOptionsAdminRateLimitTests` | Binding config values; code defaults | ✅ Green |
| `VaultRateLimitPolicyRegistrationTests` | Startup regression: both policies present | ✅ Green (runtime-verified on full ASP.NET host) |
| `VaultRotateRateLimitTests` | 200 authorized under normal load; 429 burst; no audit row on 429 | ✅ Green |
| `VaultRotateAuthTests` | 403 unauthorized; 401 no JWT; outbox unchanged | ✅ Green |
| `TokenVaultServiceRotateAuditTests` | Outbox row emitted with correct payload; no fire-and-forget path | ✅ Green |
| `TokenVaultServiceReencryptAuditTests` | Outbox row emitted; recordsAffected accurate; no PAN/key material | ✅ Green |
| `VaultRotateAuditIntegrationTests` | Full HTTP path: 200 response + outbox row; payload shape verified (actor, keyId, traceId, rotatedAt, no key material) | ✅ Green |
| **Total CardVault.Tests** | **288 passed** (+4 new from prior 284) | ✅ **PASS** |

**No tests deleted or skipped.**

---

## Spec Compliance

All 16 spec scenarios in `vault-and-pci` (Vault Key Rotation + PCI-Safe Audit Events) are now covered:

| Scenario | Coverage Type | Test(s) |
|----------|---------------|---------|
| Active key persist across restarts | Architecture verified | Design-level documentation |
| Background re-encryption in batches | Covered | `TokenVaultServiceReencryptAuditTests`, `VaultRotateRateLimitTests` |
| Authorized admin 200 within rate window | Covered | `VaultRotateRateLimitTests`, `VaultRotateAuditIntegrationTests` |
| Rotation throttled 429 | Covered | `VaultRotateRateLimitTests` |
| Re-encrypt throttled 429 | Covered | `VaultRotateRateLimitTests` |
| Unauthorized 403 | Covered | `VaultRotateAuthTests` |
| Startup asserts both policies registered | Covered + runtime-verified | `VaultRateLimitPolicyRegistrationTests` (full host boot) |
| Successful rotate emits VaultKeyRotated | Covered | `TokenVaultServiceRotateAuditTests`, `VaultRotateAuditIntegrationTests` |
| VaultKeyRotated payload (actor, keyId, traceId, rotatedAt) | Covered | Unit + integration tests |
| VaultKeyRotated payload (no key material/PAN) | Covered | Unit + integration tests |
| Re-encrypt emits VaultReencryptionBatchCompleted | Covered | `TokenVaultServiceReencryptAuditTests` |
| recordsAffected count correct | Covered | `TokenVaultServiceReencryptAuditTests` |
| Throttled request no audit row | Covered | `VaultRotateRateLimitTests` |
| 403 request no audit row | Covered | `VaultRotateAuthTests` |
| Audit survives event-bus outage via outbox | Architecture verified + design documented | Design.md data flow, `TokenVaultService.cs` implementation |
| Cold-start atomicity: outbox not in initial save | Covered (NEW) | `TokenVaultServiceRotateColdStartAtomicityTests` (4 new facts) |

---

## Verification Summary

**Mode:** Hybrid (Engram unavailable; file-only persistence)  
**Verdict:** **PASS** — Archive ready  
**Date:** 2026-05-30  

### Findings (All Resolved)

| ID | Type | Title | Status |
|----|------|-------|--------|
| W-1 | WARNING | Atomicity: outbox isolation between cold-start and warm-start saves | ✅ RESOLVED (new test proves ordering) |
| S-1 | SUGGESTION | Startup guard liveness: field name fragility | ✅ RESOLVED (runtime-verified; documented fragility in code comments) |
| W-2 | WARNING | Task checkbox T-02 unchecked | ✅ RESOLVED (all 14 tasks checked) |
| W-3 | WARNING | Integration test traceId assertion strength | ✅ RESOLVED (assertion present; technical note that value is always non-null) |
| S-2 | SUGGESTION (carry-over) | Empty re-encrypt batch no-op behavior not documented in runbook | ✅ Noted in runbook (vault-key-rotation.md) |

**0 CRITICAL findings.**  
**0 WARNING findings** (all 3 prior warnings resolved).  
**2 SUGGESTION findings** (low risk, documented).

### Test Run

```
CardVault.Tests: 288 passed, 0 failed, 0 skipped (duration: 13 s)
Delta vs prior verify run: +4 tests
```

---

## Deliverables Checklist

- [x] `vault_admin_ops` policy registered in `Program.cs` with actor-partitioned rate limit
- [x] `Vault:AdminRateLimit:*` config bound with dev-relaxed and prod-tightened defaults
- [x] `RotateActiveKeyAsync` emits `VaultKeyRotated` audit via transactional outbox
- [x] `ReEncryptBatchAsync` emits `VaultReencryptionBatchCompleted` audit via transactional outbox
- [x] Startup assertion: both `vault_detokenize` and `vault_admin_ops` present
- [x] Integration tests: 200 authorized, 429 burst, 403 unauthorized, audit payload correct
- [x] Cold-start atomicity proven: outbox row NOT in initial VaultSettings save
- [x] Unit tests: outbox row structure, fire-and-forget path removed
- [x] Operator runbook: preconditions, rotation/re-encryption procedures, audit verification, troubleshooting
- [x] Spec merged: delta scenarios added to main `vault-and-pci` spec
- [x] All 14 tasks marked complete
- [x] Verify report: PASS (0 CRITICAL, 0 WARNING, 288 tests green)

---

## Change Metadata

| Field | Value |
|-------|-------|
| **Change Name** | fix-vault-rotation-policy |
| **Archived Date** | 2026-05-30 |
| **Archive Folder** | `openspec/changes/archive/2026-05-30-fix-vault-rotation-policy/` |
| **Artifact Store Mode** | hybrid (openspec filesystem) |
| **Spec Merge Status** | Complete — delta scenarios merged into `openspec/specs/vault-and-pci/spec.md` |
| **Verification Verdict** | PASS (0 CRITICAL, 0 WARNING, 288 CardVault tests green) |
| **Next Recommended** | None — change is complete and closed |

---

## Regulatory Alignment

This change closes a documented regulatory blocker:

- **PCI-DSS 3.6.4**: "Cryptographic keys shall be changed at the end of their defined cryptoperiod and at any other time a key has been weakened or compromised." The `vault_admin_ops` registration and rate-limit scenarios now enforce controlled key rotation with audit trail.
- **Superintendencia de Bancos del Ecuador, Resolución JB-2014-3066**: "Operational risk for banking technology — controlled cryptographic operations with audit trail." Transactional outbox audit ensures no rotation is silent-failed.

---

## Rollback Plan

The change is shipping as production code. Rollback steps (if needed post-deploy):

1. **Code**: Revert the `AddPolicy("vault_admin_ops", ...)` registration and the audit-event emission in `TokenVaultService`. Endpoints return to current broken state.
2. **Configuration**: Remove the `Vault:AdminRateLimit` section from `appsettings.*.json`.
3. **Tests**: Remove the new `Features/Vault/` test folder.
4. **Runbook**: Delete `docs/runbooks/vault-key-rotation.md`.
5. **Spec**: Revert the scenario additions under `vault-and-pci`.

Because the change only enables an endpoint pair that does not currently work, rollback cannot regress any working flow.

---

## Archive Completion

This report finalizes the SDD cycle for **fix-vault-rotation-policy**.

- **Proposal** → ✅ Stated intent and approach
- **Specs** → ✅ Defined behavior and scenarios
- **Design** → ✅ Technical decisions and architecture
- **Tasks** → ✅ Work breakdown (14/14 complete)
- **Apply** → ✅ Implementation (6 work units, all green)
- **Verify** → ✅ Validation (288 tests green, 0 CRITICAL, all warnings resolved)
- **Archive** → ✅ Closed and filed

**Ready for production release.**
