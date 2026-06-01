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

## Status: COMPLETE
Task 1e.1 is fully implemented. Next: Task 1e.2 (webhook endpoint + rate-limit) — separate PR.
