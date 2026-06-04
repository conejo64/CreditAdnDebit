# Apply Progress — Slice 1e.2 Hardening

**Change**: real-notification-channels
**Slice**: 1e.2 — Security hardening from verify-1e2 findings
**Branch**: feat/notifications-1e1-webhook-validators
**Date**: 2026-06-03

---

## TDD Cycle

### CRIT-2 — Discriminated validator result (RED → GREEN)

**RED**: Changed `IWebhookSignatureValidator.Validate()` return type from `bool` to
`WebhookValidationResult` in `INotificationProvider.cs`. This caused 3 compile errors
(Twilio, SendGrid, Movistar validators) and the test project stub. Confirmed RED state
before writing any implementation.

**GREEN**:
- Added `WebhookValidationResult { Valid, MissingSignature, InvalidSignature, Replayed }` enum
- Updated all 3 validators to return `Replayed` for timestamp violations and `InvalidSignature`
  for HMAC/ECDSA mismatches
- Controller updated: explicit header-presence check retained (step 3), `Validate()` result
  mapped to audit reason via switch expression
- Body stream `Position = 0` reset after `CopyToAsync` (WARN-1)
- PCI audit subject changed to `confirmedDelivery.Id.ToString()` (WARN-2); event only fires
  when a row is confirmed
- `catch {}` narrowed to `catch (JsonException)` (SUGG-1)
- `FakeWebhookSignatureValidator` updated: convenience `bool` constructor preserved,
  primary constructor takes `WebhookValidationResult`
- `StubWebhookValidator` in `NotificationProviderContractTests.cs` updated

**Tests updated**: All 1e.1 unit tests in `WebhookSignatureValidatorTests.cs` assert on
`WebhookValidationResult` enum values (not `.BeTrue()`/`.BeFalse()`). Replay tests assert
`Replayed`, missing-header tests assert `MissingSignature`.

**New integration tests**:
- `ReplayedRequest_Returns401_WithReplayedAuditReason` — fake returns `Replayed`; asserts
  audit payload contains "replayed" and NOT "invalid-signature"
- `TamperedSignature_WithNewResult_Returns401_WithInvalidSignatureAuditReason` — fake returns
  `InvalidSignature`; asserts audit payload contains "invalid-signature"
- `TwilioFormEncodedCallback_Returns200` — WARN-1: form-encoded POST through endpoint

**Commit**: `8d88fbe`

---

### CRIT-1 — Rate-limit config (RED → GREEN)

**RED**: The `Notifications:Webhook:RateLimits` section was absent from all appsettings files.
All providers fell back to 100/min (sendgrid/twilio under-permissive; unknown over-permissive
vs spec).

**GREEN**:
- Added `"Webhook": { "RateLimits": { "sendgrid": 600, "twilio": 300, "movistar-ec": 120, "unknown": 60 } }`
  to `appsettings.json`
- Changed fallback in `Program.cs` from `100` to `60` (deny-tighter-by-default)
- No secrets added

**New tests**:
- `RateLimitExceeded_SendGrid_Returns429OnSecondRequest` (SUGG-2)
- `RateLimitFallback_UnknownProvider_Uses60NotDefault100` (CRIT-1 config key roundtrip)

**Commit**: `197d44d`

---

### WARN/SUGG Cleanups

- **WARN-4**: Rate-limit test config key changed from `"Twilio"` (capital T) to `"twilio"` (lowercase) in `WebhookEndpointIntegrationTests.cs`

**Commit**: `e4af7b5`

---

## Test Results

| Run | CardVault | IsoSwitch | Total | Failures |
|-----|-----------|-----------|-------|---------|
| Baseline (1e.1) | 554 | 37 | 591 | 0 |
| After 1e.2 | 559 | 37 | 596 | 0 |

Net new tests: +5

---

## Files Changed

| File | Change |
|------|--------|
| `src/CardVault.Api/Services/Notifications/INotificationProvider.cs` | Added `WebhookValidationResult` enum; changed `Validate()` return type |
| `src/CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookSignatureValidator.cs` | Returns `Replayed`/`InvalidSignature`/`MissingSignature` |
| `src/CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookSignatureValidator.cs` | Same |
| `src/CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookSignatureValidator.cs` | Same |
| `src/CardVault.Api/Controllers/NotificationsController.cs` | Switch on result, stream reset, PCI subject, JsonException |
| `src/CardVault.Api/Program.cs` | Fallback 100→60 |
| `src/CardVault.Api/appsettings.json` | Added RateLimits section |
| `tests/.../FakeWebhookSignatureValidator.cs` | New `WebhookValidationResult` constructor |
| `tests/.../WebhookSignatureValidatorTests.cs` | All assertions updated to enum values |
| `tests/.../WebhookEndpointIntegrationTests.cs` | New tests: replay, form-encoded, SendGrid 429, lowercase key |
| `tests/.../NotificationProviderContractTests.cs` | StubWebhookValidator updated |
