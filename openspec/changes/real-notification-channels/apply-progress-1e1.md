# Apply Progress — Slice 1e.1: Webhook Signature Validators
## Change: real-notification-channels
## Date: 2026-06-01
## Branch: feat/notifications-1e1-webhook-validators

---

## TDD Cycle Summary

### RED Phase
Wrote `WebhookSignatureValidatorTests.cs` with 27 tests covering all three validators
before any production code existed. Build failed with 4 CS errors (missing types).

### GREEN Phase
Created 7 production files:
- `TwilioWebhookOptions.cs` — options DTO binding `AuthToken` + `WebhookUrl` from env vars
- `SendGridWebhookOptions.cs` — options DTO binding `WebhookPublicKeyPem` from env vars
- `MovistarWebhookOptions.cs` — options DTO binding `WebhookSecret` from env vars
- `TwilioWebhookSignatureValidator.cs` — HMAC-SHA1, form-params sorted, constant-time
- `SendGridWebhookSignatureValidator.cs` — ECDSA P-256/SHA-256, DER sig, constant-time
- `MovistarWebhookSignatureValidator.cs` — HMAC-SHA256, hex-encoded, case-insensitive, constant-time

All 27 tests passed immediately.

### REFACTOR Phase
Extracted `WebhookValidatorHelper.IsWithinReplayWindow` to de-duplicate the 5-minute
replay-guard logic across the three validators. Verified all 27 tests still green.

---

## Key Decisions

### IWebhookSignatureValidator location
Interface already defined in `INotificationProvider.cs` (Slice 1a). Implementations
placed in new `CardVault.Api/Services/Notifications/Webhooks/` subdirectory.

### SendGrid: ECDSA not HMAC
The spec says "HMAC" but the design doc (§8) explicitly corrects this to ECDSA P-256.
Used `ECDsa.Create()` + `ImportSubjectPublicKeyInfo` + `VerifyData` with
`DSASignatureFormat.Rfc3279DerSequence` (DER-encoded signature per SendGrid docs).
No additional NuGet packages needed — `System.Security.Cryptography` in .NET 9 covers this.

### Replay boundary: < 300 seconds (strict less-than)
The spec says "timestamp > 5 min old". Implemented as `age.TotalSeconds >= 300` → reject,
meaning exactly 5 min is also rejected. A test `Validate_TimestampExactly5MinAgo_ReturnsFalse`
documents this boundary explicitly.

### Twilio form params: sorted by ASCII key order
Twilio's algorithm requires params sorted by key (ASCII/ordinal order), concatenated as
`key1value1key2value2...`. The validator reads `IFormCollection` which is already loaded by
ASP.NET Core middleware before the validator runs.

### Movistar hex comparison: case-insensitive
Movistar Ecuador's B2B contract may send uppercase or lowercase hex. The validator
normalizes both sides to uppercase before constant-time comparison.

### Constant-time comparison
All three validators use `CryptographicOperations.FixedTimeEquals`. Length mismatch
exits early (unavoidable for security since hex/base64 lengths encode the hash function)
but runs a dummy `FixedTimeEquals` to prevent branch elimination from leaking length info.

---

## Test Coverage

| Validator | Tests | Scenarios |
|-----------|-------|-----------|
| TwilioWebhookSignatureValidator | 8 | positive (no params), positive (with params), missing header, tampered sig, tampered body, replay > 5min, exactly 5min, just under 5min, ProviderId |
| SendGridWebhookSignatureValidator | 8 | positive, wrong key, tampered body, missing sig header, missing ts header, replay, just under 5min, ProviderId |
| MovistarWebhookSignatureValidator | 11 | positive, wrong secret, tampered body, missing header, replay, exactly 5min, just under 5min, uppercase hex accepted, ProviderId |

**Total webhook tests: 27**
**Full suite: 540 (503 CardVault + 37 IsoSwitch), 0 failures**
**Baseline was: 514 (477 CardVault + 37 IsoSwitch)**

---

## Files Changed

### Production (new)
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookOptions.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookOptions.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookOptions.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookSignatureValidator.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookSignatureValidator.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookSignatureValidator.cs`
- `backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/WebhookValidatorHelper.cs`

### Tests (new)
- `backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Webhooks/WebhookSignatureValidatorTests.cs`

### Artifacts updated
- `openspec/changes/real-notification-channels/tasks.md` — Task 1e.1 checked off
- `openspec/changes/real-notification-channels/apply-progress-1e1.md` — this file

---

## DI Registration (NOT done in this task)
The three validators need to be registered as keyed services in `Program.cs`:
```csharp
services.AddKeyedSingleton<IWebhookSignatureValidator, TwilioWebhookSignatureValidator>("twilio");
services.AddKeyedSingleton<IWebhookSignatureValidator, SendGridWebhookSignatureValidator>("sendgrid");
services.AddKeyedSingleton<IWebhookSignatureValidator, MovistarWebhookSignatureValidator>("movistar-ec");
```
This is deferred to Task 1e.2 (endpoint), which also registers the rate-limit policy.

---

---

## Hardening Cycle — Slice 1e.1 (verify findings) — 2026-06-02

### RED Phase
Added 6 new tests before any production change:
- `Validate_FutureDatedTimestamp_ReturnsFalse` × 3 (Twilio, SendGrid, Movistar) — CRIT-1
- `Validate_MultiValueParamSortedAlphabetically_ReturnsTrue` (Twilio) — WARN-1
- `Validate_MissingTimestampHeader_ReturnsFalse` × 2 (Twilio, Movistar) — SUGG-2

Confirmed RED: 3 FutureDated tests failed (accepted future timestamps); 1 MultiValue test
failed (insertion-order concat didn't match sorted-value concat). MissingTimestamp tests
already passed (existing guard fires first when both headers absent — coverage gap closed).

### GREEN Phase

**CRIT-1** (`WebhookValidatorHelper.cs:19`):
Changed `ageSeconds < ReplayWindowSeconds` → `ageSeconds >= 0 && ageSeconds < ReplayWindowSeconds`.
All 3 FutureDated tests went GREEN. All 27 existing replay/boundary tests stayed GREEN.

**WARN-1** (`TwilioWebhookSignatureValidator.cs` — `BuildSortedParamString`):
Changed `kv.Value.ToString()` → `string.Concat(kv.Value.OrderBy(v => v, StringComparer.Ordinal))`.
MultiValue test went GREEN. Existing single-value tests stayed GREEN.

**WARN-2** (Twilio + Movistar):
Dummy `FixedTimeEquals(actualBytes, actualBytes)` → `FixedTimeEquals(expectedBytes, expectedBytes)`.
Behavioral parity — no test change needed; covered by existing tests.

**WARN-3** (Twilio `BuildSortedParamString`):
Removed bare `catch {}` that returned `string.Empty`. Now `InvalidOperationException` propagates
to `Validate()` which catches it specifically and returns `false` (fail-closed).

**WARN-4** (`SendGridWebhookSignatureValidator`):
Replaced per-request PEM parse + `ECDsa.Create()` + `ImportSubjectPublicKeyInfo` with a
constructor-time parse that caches `ECParameters`. Per-call verifier is `ECDsa.Create(ecParams)`
(struct copy, no PEM/DER work, thread-safe via separate instances).

**SUGG-1** (`SendGridWebhookSignatureValidator.ImportPemPublicKey`):
Removed contradictory comment that mentioned `ImportFromPem`; comment now matches the
actual `ImportSubjectPublicKeyInfo` call.

### REFACTOR Phase
N/A — all changes were targeted fixes.

### Test Results After Hardening
**Full suite: 546 (509 CardVault + 37 IsoSwitch), 0 failures**
Previous baseline: 540. Delta: +6 tests.

### Commits
- `6e9913c` — `fix(security): reject future-dated webhook timestamps in replay guard (CRIT-1)` (CRIT-1 + WARN-1 + new tests)
- `04c6746` — `fix(security): harden webhook validators -- timing-oracle and fail-closed (WARN-2/WARN-3)`
- `f89d51a` — `refactor(security): harden webhook validators — WARN-2/3/4 + SUGG-1`

---

## Status: COMPLETE (HARDENED)
Task 1e.1 implemented and all verify findings resolved. Next: Task 1e.2 (webhook endpoint + rate-limit) — separate PR.
