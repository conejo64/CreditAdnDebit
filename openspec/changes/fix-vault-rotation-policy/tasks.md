# Tasks: fix-vault-rotation-policy

> Strict TDD active. Test runner: `dotnet test backend/CardSwitchPlatform.sln`
> Delivery strategy: auto-chain
> Every slice: write failing test → implement → green → refactor

---

## Work Unit 1 — Config model + policy options (foundation)

> Sequential prerequisite for all other units. No HTTP layer yet; pure model + config.

**Spec links:** `vault-and-pci` §Vault Key Rotation — rate-limit scenarios; §Startup asserts vault_admin_ops policy is registered

### T-01 — Extend `VaultOptions` with `AdminRateLimit` nested options

- [x] **TEST (RED):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/VaultOptionsAdminRateLimitTests.cs`.
  - Assert that binding `{ "PermitLimit": 5, "WindowSeconds": 300, "QueueLimit": 0 }` onto `VaultOptions.AdminRateLimit` produces the expected values.
  - Assert that an unbound instance has non-zero code defaults (`PermitLimit >= 1`, `WindowSeconds >= 1`).
- [x] **IMPLEMENT:** In `backend/services/CardVault/src/CardVault.Api/Vault/VaultCrypto.cs`, add a nested `AdminRateLimitOptions` class (PermitLimit, WindowSeconds, QueueLimit with dev-relaxed defaults) and an `AdminRateLimit` property on `VaultOptions` (line ~92, after the existing properties).
- [x] **GREEN / REFACTOR:** Run `dotnet test backend/CardSwitchPlatform.sln --filter "FullyQualifiedName~VaultOptionsAdminRateLimitTests"` — must be green.

### T-02 — Add `Vault:AdminRateLimit` section to appsettings files

- [ ] **IMPLEMENT (no test needed — config correctness is exercised by T-03/T-04):**
  - `backend/services/CardVault/src/CardVault.Api/appsettings.json`: add `"Vault": { "AdminRateLimit": { "PermitLimit": 20, "WindowSeconds": 60, "QueueLimit": 0 } }` (dev-relaxed defaults).
  - **Create** `backend/services/CardVault/src/CardVault.Api/appsettings.Production.json`: add `"Vault": { "AdminRateLimit": { "PermitLimit": 5, "WindowSeconds": 300, "QueueLimit": 0 } }` (tightened prod values).
  - Add an INFO startup log in `Program.cs` (after rate-limiter registration) emitting the bound limits.

---

## Work Unit 2 — Rate-limit policy registration + startup assertion

> Sequential after WU1 (needs the options model). Parallel with WU3 is possible but WU3 depends on WU2.

**Spec links:** `vault-and-pci` §Scenario: Authorized admin rotates key within rate window; §Scenario: Rotation request is throttled on burst; §Scenario: Startup asserts vault_admin_ops policy is registered

### T-03 — Startup regression test: both policies must be registered at boot

- [x] **TEST (RED):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/VaultRateLimitPolicyRegistrationTests.cs`.
  - Boot the application host (use `CardVaultWebApplicationFactory`).
  - Resolve `Microsoft.AspNetCore.RateLimiting.RateLimiterOptions` (or the equivalent host service).
  - Assert that both `"vault_detokenize"` and `"vault_admin_ops"` are present in the policy registry.
  - This test MUST be red before T-04.
- [x] **IMPLEMENT:** In `Program.cs` lines 163-166, add `AddPolicy("vault_admin_ops", httpContext => RateLimitPartition.GetFixedWindowLimiter(...)` binding `PermitLimit / Window / QueueLimit` from `vaultOpt.AdminRateLimit`.
  - Partition key: `httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon"` (mirrors `vault_detokenize`).
  - After `builder.Build()`, add a startup assertion block: resolve `RateLimiterOptions`; throw `InvalidOperationException` with a descriptive message if either `vault_detokenize` or `vault_admin_ops` is absent.
  - Add `logger.LogInformation("Vault admin rate-limit bound: {PermitLimit} req / {Window}s / queue {Queue}", ...)`.
- [x] **GREEN / REFACTOR:** Run test filter `VaultRateLimitPolicyRegistrationTests` — green.

### T-04 — Integration test: 200 under normal load, 429 under burst

- [x] **TEST (RED):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/VaultRotateRateLimitTests.cs`.
  - **Scenario A (200 normal):** `POST /api/vault/rotate-active-key` with Admin JWT and a valid key body within permit limit → assert `200 OK`.
  - **Scenario B (429 burst):** Override `Vault:AdminRateLimit:PermitLimit` to `1` via `WithWebHostBuilder` config override. Fire 2 requests in sequence; assert the second returns `429 Too Many Requests`.
  - **Scenario C (429 reencrypt burst):** Same pattern for `POST /api/vault/reencrypt`.
  - Use `CardVaultWebApplicationFactory` extended with per-test config override (pass `IWebHostBuilder.UseSetting` or `ConfigureAppConfiguration`).
  - Verify that 429 responses add NO new `OutboxMessageEntity` rows (query `CardVaultDbContext.OutboxMessages.Count()` before and after).
- [x] **GREEN / REFACTOR:** Ensure tests pass after T-03 implementation is in place.

### T-05 — Integration test: 403 unauthorized caller

- [x] **TEST (RED):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/VaultRotateAuthTests.cs`.
  - **Scenario A:** JWT without `CanRotateVaultKeys` (Auditor role) on `POST /api/vault/rotate-active-key` → assert `403 Forbidden`.
  - **Scenario B:** Same for `POST /api/vault/reencrypt`.
  - **Scenario C:** No JWT at all → assert `401 Unauthorized` (optional but triangulates boundary).
  - Verify outbox row count unchanged after a 403 request.
- [x] **GREEN / REFACTOR:** This test should already be green if the existing `[Authorize(Policy = "CanRotateVaultKeys")]` attribute is intact; if red, diagnose the controller attributes rather than changing policy logic.

---

## Work Unit 3 — Transactional outbox audit emission

> Sequential after WU2 (T-03 must be green so the host boots without error during integration tests). WU3 is the core behavioral change.

**Spec links:** `vault-and-pci` §PCI-Safe Audit Events — all scenarios; §Scenario: Audit event survives transient event-bus outage via outbox; §Scenario: Audit event is not emitted on failed or throttled rotation

### T-06 — Unit test: `RotateActiveKeyAsync` writes outbox row (RED before T-07)

- [x] **TEST (RED):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/TokenVaultServiceRotateAuditTests.cs`.
  - Instantiate `TokenVaultService` with an InMemory `CardVaultDbContext` (via `TestDbContextFactory`) and `NullEventBus`.
  - Call `RotateActiveKeyAsync("k2", "test-actor", "trace-001", ct)`.
  - Assert exactly **one** `OutboxMessageEntity` exists in `db.OutboxMessages` with:
    - `Topic == "sw.cardvault.audit"`
    - `PayloadJson` deserializes to an object containing `type == "cardvault.vault.rotate"`, `actor == "test-actor"`, `keyId` present and not empty, `traceId == "trace-001"`, `rotatedAt` present and UTC.
  - Assert payload does NOT contain any of the words `"key"`, `"secret"`, `"raw"`, `"b64"`, `"cipher"`, `"nonce"` in key material positions (no key bytes leaked).
  - Assert `_bus.PublishAsync` (the fire-and-forget path) is NOT called for the rotation audit (confirm NullEventBus received 0 calls — or remove that path in T-07).
- [x] **TEST (RED):** Add scenario in same file: call `RotateActiveKeyAsync` then `ReEncryptBatchAsync` → assert each produces its own outbox row (2 rows total after both calls).

### T-07 — Implement outbox audit in `TokenVaultService.RotateActiveKeyAsync`

- [x] **IMPLEMENT:** In `backend/services/CardVault/src/CardVault.Api/Vault/TokenVaultService.cs`:
  - In `RotateActiveKeyAsync`: **remove** the existing `await _bus.PublishAsync(...)` call and the `await _pciAudit.PublishAsync(...)` call that constitute the non-transactional double-emission for rotation audit.
  - Add `_db.OutboxMessages.Add(new OutboxMessageEntity { Topic = "sw.cardvault.audit", Key = "vault", PayloadJson = JsonSerializer.Serialize(new { type = "cardvault.vault.rotate", actor, keyId = _crypto.ActiveKeyId, traceId, rotatedAt = rotatedOn }) })` immediately before `SaveChangesAsync` so the audit row is committed atomically with the state change.
  - The `SaveChangesAsync` that persists `VaultSettings` must include the outbox row in the same call (check `_settings.SetActiveKeyIdAsync` — if it calls its own `SaveChangesAsync` internally, move the outbox add to before that call or use a coordinated save; prefer batching both into one `SaveChangesAsync` on `_db`).
- [x] **GREEN / REFACTOR:** Run `TokenVaultServiceRotateAuditTests` — green.

### T-08 — Unit test: `ReEncryptBatchAsync` writes outbox row (RED before T-09)

- [x] **TEST (RED):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/TokenVaultServiceReencryptAuditTests.cs`.
  - Seed 3 `TokenVaultEntryEntity` rows with `KeyId = "k1"` in InMemory DB; active key is `"k2"`.
  - Call `ReEncryptBatchAsync(10, "system-job", "job-trace", ct)`.
  - Assert exactly **one** `OutboxMessageEntity` with:
    - `Topic == "sw.cardvault.audit"`
    - `PayloadJson` deserializes to `type == "cardvault.reencrypt.batch"`, `actor == "system-job"`, `recordsAffected == 3`, `traceId == "job-trace"`, `completedAt` present and UTC.
  - Assert payload does NOT contain raw PAN, key bytes, or cipher material.
  - **Actor is `system-job`** — this confirms the scheduler path emits audit (resolved design decision #1).

### T-09 — Implement outbox audit in `TokenVaultService.ReEncryptBatchAsync`

- [x] **IMPLEMENT:** In `ReEncryptBatchAsync`:
  - **Remove** the existing `await _bus.PublishAsync(...)` and `await _pciAudit.PublishAsync(...)` calls for the batch audit.
  - Add `_db.OutboxMessages.Add(new OutboxMessageEntity { Topic = "sw.cardvault.audit", Key = "vault", PayloadJson = JsonSerializer.Serialize(new { type = "cardvault.reencrypt.batch", actor, traceId, recordsAffected = updated, completedAt = DateTimeOffset.UtcNow }) })` **inside** the `if (updated > 0)` block, before the existing `SaveChangesAsync`.
  - Confirm the outbox add and the entity state save share the same `SaveChangesAsync` call.
  - Keep `UpdateReencryptStateAsync` call after save (no functional change to scheduler state tracking).
- [x] **GREEN / REFACTOR:** Run `TokenVaultServiceReencryptAuditTests` — green.

---

## Work Unit 4 — End-to-end audit integration tests

> Sequential after WU3 (needs the implemented service). Verifies the full HTTP path including outbox persistence.

**Spec links:** `vault-and-pci` §Scenario: Successful key rotation emits a VaultKeyRotated audit event; §Scenario: Re-encryption batch emits a VaultReencryptionBatchCompleted audit event; §Scenario: Audit event is not emitted on failed or throttled rotation

### T-10 — Integration test: successful rotate emits outbox row with correct payload

- [x] **TEST (RED → GREEN):** Add `backend/services/CardVault/tests/CardVault.Tests/Features/Vault/VaultRotateAuditIntegrationTests.cs`.
  - Boot `CardVaultWebApplicationFactory`.
  - `POST /api/vault/rotate-active-key` with Admin JWT and `{ "newActiveKeyId": "k1" }` body (or the controller's expected shape).
  - Resolve `CardVaultDbContext` from the test scope.
  - Assert exactly one `OutboxMessageEntity` with `Topic == "sw.cardvault.audit"` exists.
  - Deserialize `PayloadJson`; assert `type == "cardvault.vault.rotate"`, `actor` is the test user's identity, `keyId` is non-empty, `rotatedAt` is a valid ISO-8601 UTC string.
  - Assert payload contains none of: key bytes (Base64 patterns > 20 chars are a red flag), PAN-like strings, `nonceB64`, `cipherB64`, `tagB64`.

### T-11 — Integration test: successful reencrypt emits outbox row

- [x] **TEST:** Add or extend `VaultRotateAuditIntegrationTests.cs`.
  - `POST /api/vault/reencrypt` with Admin JWT.
  - Assert one `OutboxMessageEntity` with `type == "cardvault.reencrypt.batch"`, `recordsAffected` is an integer, `completedAt` present.

### T-12 — Integration test: throttled request produces no audit row

- [x] **TEST:** Add or extend `VaultRotateRateLimitTests.cs` (WU2).
  - Override `PermitLimit` to `1`, fire 2 requests to `POST /api/vault/rotate-active-key`.
  - Assert first request: `200 OK`, 1 outbox row.
  - Assert second request: `429`, outbox row count still 1 (no new row added).

---

## Work Unit 5 — Spec update

> Can run in parallel with WU1-WU4 (it is a doc change, no code dependency). However, recommend sequencing after WU3 is green so the spec text reflects exactly what was implemented.

**Spec links:** All delta scenarios in `openspec/changes/fix-vault-rotation-policy/specs/vault-and-pci/spec.md`

### T-13 — Apply delta spec to the base vault-and-pci spec

- [x] **IMPLEMENT:** Merge the delta scenarios from `openspec/changes/fix-vault-rotation-policy/specs/vault-and-pci/spec.md` into `openspec/specs/vault-and-pci/spec.md`:
  - Under **Vault Key Rotation**: add the four explicit scenarios (authorized within window → 200, burst → 429, re-encrypt burst → 429, unauthorized → 403) and the startup assertion scenario.
  - Under **PCI-Safe Audit Events**: add the four explicit scenarios (successful rotate emits `VaultKeyRotated`, batch emits `VaultReencryptionBatchCompleted`, failed/throttled → no event, outbox durability).
  - Do not remove or alter any existing passing scenario.

---

## Work Unit 6 — Operator runbook

> Can run in parallel with WU3/WU4 (pure documentation). Must ship in the same PR slice as the implementation it documents.

**Spec links:** Proposal §In Scope — operator runbook

### T-14 — Create vault-key-rotation runbook

- [x] **CREATE:** `backend/services/CardVault/docs/runbooks/vault-key-rotation.md` with the following sections:
  1. **Purpose** — why rotation is required (PCI-DSS 3.6.4, SB Resolución JB-2014-3066).
  2. **Preconditions** — confirm the new key ID is provisioned in `Vault:Keys`; verify current `vault_admin_ops` rate-limit config; confirm `EfOutboxPublisher` hosted service is running and Kafka is reachable.
  3. **Pre-rotation rate-limit override** — how to set `Vault:AdminRateLimit:PermitLimit` to a higher value during the rotation window without redeploy (environment variable override).
  4. **Rotation procedure** — step-by-step: `POST /api/vault/rotate-active-key` with required headers and body; expected `200 OK` response shape.
  5. **Re-encryption procedure** — how to call `POST /api/vault/reencrypt` in batches; how to monitor via `GET /health/vault` (`lastReencryptStatus`, `lastReencryptUpdated`); how to verify `OutboxMessages` table for audit rows.
  6. **Audit verification** — query `sw.cardvault.audit` Kafka topic or `outbox_messages` table for `cardvault.vault.rotate` and `cardvault.reencrypt.batch` events; confirm no key material in payload.
  7. **Rollback** — revert `Vault:ActiveKeyId` config and redeploy; re-encrypt back if needed; rollback code steps.
  8. **Troubleshooting** — 429 during rotation (rate-limit override), 403 (missing `CanRotateVaultKeys` claim), audit row missing (check `EfOutboxPublisher` errors), startup `InvalidOperationException` (policy not registered — check `Program.cs`).

---

## Sequencing Summary

```
WU1 (T-01, T-02)
  └─→ WU2 (T-03, T-04, T-05)  [sequential: needs VaultOptions model]
        └─→ WU3 (T-06→T-09)   [sequential: needs host to boot cleanly]
              └─→ WU4 (T-10, T-11, T-12) [sequential: needs service implementation]

WU5 (T-13)  ─ can start after WU3 green, parallel to WU4
WU6 (T-14)  ─ parallel with WU3/WU4; commit with WU4 in final PR
```

**Critical path:** T-01 → T-03 → T-06 → T-07 → T-09 → T-10

---

## Files Affected

| File | Action | Work Unit |
|------|--------|-----------|
| `CardVault.Api/Vault/VaultCrypto.cs` | Modify — add `AdminRateLimitOptions` + property on `VaultOptions` | WU1/T-01 |
| `CardVault.Api/appsettings.json` | Modify — add `Vault:AdminRateLimit` dev section | WU1/T-02 |
| `CardVault.Api/appsettings.Production.json` | **Create** — add `Vault:AdminRateLimit` prod section | WU1/T-02 |
| `CardVault.Api/Program.cs` | Modify — `AddPolicy("vault_admin_ops")` + startup assertion + INFO log | WU2/T-03 |
| `CardVault.Api/Vault/TokenVaultService.cs` | Modify — replace fire-and-forget audit with outbox add in both methods | WU3/T-07,T-09 |
| `tests/.../Features/Vault/VaultOptionsAdminRateLimitTests.cs` | **Create** | WU1/T-01 |
| `tests/.../Features/Vault/VaultRateLimitPolicyRegistrationTests.cs` | **Create** | WU2/T-03 |
| `tests/.../Features/Vault/VaultRotateRateLimitTests.cs` | **Create** | WU2/T-04 |
| `tests/.../Features/Vault/VaultRotateAuthTests.cs` | **Create** | WU2/T-05 |
| `tests/.../Features/Vault/TokenVaultServiceRotateAuditTests.cs` | **Create** | WU3/T-06 |
| `tests/.../Features/Vault/TokenVaultServiceReencryptAuditTests.cs` | **Create** | WU3/T-08 |
| `tests/.../Features/Vault/VaultRotateAuditIntegrationTests.cs` | **Create** | WU4/T-10,T-11,T-12 |
| `openspec/specs/vault-and-pci/spec.md` | Modify — merge delta scenarios | WU5/T-13 |
| `CardVault.Api/docs/runbooks/vault-key-rotation.md` | **Create** | WU6/T-14 |

---

## Review Workload Forecast

| Metric | Estimate |
|--------|----------|
| Production code changed lines | ~70 (VaultCrypto.cs ~15, Program.cs ~20, TokenVaultService.cs ~25, appsettings ~10) |
| Test code added lines | ~280 (7 new test files × avg 40 lines) |
| Docs added lines | ~80 (runbook + spec delta) |
| **Total changed/added lines** | **~430** |
| 400-line budget risk | **Medium** |
| Chained PRs recommended | **No** — total is just above 400 but >60% is test code; single PR is appropriate. The production diff is well under 100 lines, which is the meaningful complexity measure. The slight over-budget is in test additions that belong with their behavior. Flag as `size:accepted-test-heavy` on PR creation. |

**Decision for auto-chain mode:** Proceed as a **single PR** with work-unit commits. The production change is tiny (~70 lines); the test-heavy total slightly exceeds 400 but does not justify splitting test code from its implementation. Ship as one PR with `size:accepted-test-heavy` label.

Commit sequence (each a candidate chained PR if the team later decides to split):

1. `feat(vault): add AdminRateLimit options to VaultOptions and default config` (WU1)
2. `feat(vault): register vault_admin_ops rate-limit policy with startup assertion` (WU2)
3. `feat(vault): route rotation and re-encrypt audit through transactional EF outbox` (WU3+WU4)
4. `docs(vault): update vault-and-pci spec with rate-limit and audit scenarios` (WU5)
5. `docs(vault): add vault-key-rotation operator runbook` (WU6)
