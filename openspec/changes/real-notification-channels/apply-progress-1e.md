# Apply Progress — Slice 1e
## Change: `real-notification-channels`
## Date: 2026-06-03
## Mode: Standard (no Strict TDD module; integration tests written first and confirmed RED before GREEN)

---

## TDD Cycle Evidence

| Task | RED | GREEN | REFACTOR |
|------|-----|-------|----------|
| 1e.1 `IWebhookSignatureValidator` + validators | ✅ 33 unit tests written → FAIL | ✅ Implementations → PASS (33/33) | N/A |
| 1e.2 Webhook endpoint + rate-limit + audit | ✅ 7 integration tests written → FAIL (6 failures, 1 accidental pass) | ✅ Endpoint + DI registrations → PASS (7/7) | N/A |

---

## Completed Tasks

- [x] **1e.1** — `IWebhookSignatureValidator` interface + `SignatureHeaderName` property + `TwilioWebhookSignatureValidator` + `SendGridWebhookSignatureValidator` + `MovistarWebhookSignatureValidator` + `WebhookValidatorHelper` (replay guard). 33 unit tests passing.
- [x] **1e.2** — `POST /api/notifications/delivery-callback/{providerId}` endpoint in `NotificationsController`; `notifications_webhook` rate-limit policy; keyed DI registrations for all three validators; `StubWebhookValidator.SignatureHeaderName` fix in contract tests. 7 integration tests + 524 total suite passing.

---

## Files Changed

| File | Action | Description |
|------|--------|-------------|
| `CardVault.Api/Services/Notifications/INotificationProvider.cs` | Modified | Added `string SignatureHeaderName { get; }` to `IWebhookSignatureValidator` interface |
| `CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookSignatureValidator.cs` | Modified | Implemented `SignatureHeaderName => "X-Twilio-Signature"` |
| `CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookSignatureValidator.cs` | Modified | Implemented `SignatureHeaderName => SignatureHeader` |
| `CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookSignatureValidator.cs` | Modified | Implemented `SignatureHeaderName => "X-Movistar-Signature"` |
| `CardVault.Api/Controllers/NotificationsController.cs` | Modified | Added `DeliveryCallback` action: `[AllowAnonymous]`, `[EnableRateLimiting]`, raw body read, signature validation, `DeliveredOn` update, PCI audit |
| `CardVault.Api/Program.cs` | Modified | Added `using Webhooks;`, `TwilioWebhookOptions`/`SendGridWebhookOptions`/`MovistarWebhookOptions` config, keyed singleton validators, `notifications_webhook` rate-limit policy |
| `CardVault.Tests/Features/Notifications/Webhooks/WebhookSignatureValidatorTests.cs` | Created (1e.1) | 33 unit tests for all three validators |
| `CardVault.Tests/Features/Notifications/Webhooks/FakeWebhookSignatureValidator.cs` | Created | Test double with `SignatureHeaderName = "X-Fake-Signature"`, configurable result |
| `CardVault.Tests/Features/Notifications/Webhooks/WebhookEndpointIntegrationTests.cs` | Created | 7 integration tests covering happy paths, security rejection, 404, rate-limit |
| `CardVault.Tests/Features/Notifications/Abstractions/NotificationProviderContractTests.cs` | Modified | Added `SignatureHeaderName => "X-Stub-Signature"` to `StubWebhookValidator` |
| `openspec/changes/real-notification-channels/tasks.md` | Modified | Marked 1e.1 and 1e.2 `[x]` complete |

---

## Key Decisions Made During Apply

1. **`SignatureHeaderName` on `IWebhookSignatureValidator`**: additive property enabling the controller to distinguish `missing-signature` (header absent) from `invalid-signature` (header present but `Validate()` returns false). Non-breaking: all existing implementors updated.

2. **Keyed DI + test shadowing**: Real validators registered as `AddKeyedSingleton` in `Program.cs`; test `ConfigureTestServices` registers `FakeWebhookSignatureValidator` instances LAST — last-registration wins in .NET keyed DI, so fakes shadow real validators without modifying production code.

3. **`notifications_webhook` rate-limit reads from config at request time**: Closure captures `builder.Configuration` (live reference); tests override via `b.UseSetting("Notifications:Webhook:RateLimits:Twilio", "1")` — works because `WithWebHostBuilder` re-runs `Program.cs` with the overridden config, and `IConfiguration` key lookup is case-insensitive.

4. **`SendGridWebhookSignatureValidator` never instantiated in tests**: Its constructor throws `CryptographicException` on empty PEM, but since fakes are registered LAST for the "sendgrid" key, the real factory is never invoked during test runs.

5. **`DeliveredOn` update filter**: `d.ProviderReference == providerReference && d.DeliveredOn == null` — idempotent; duplicate callbacks don't overwrite an already-set timestamp.

---

## Deviations from Design

None — implementation matches design.md and spec.

---

## Test Results

| Run | Tests | Passed | Failed |
|-----|-------|--------|--------|
| Before 1e.1 | 484 | 484 | 0 |
| After 1e.1 | 517 | 517 | 0 |
| RED (1e.2 tests written) | 524 | 518 | 6 |
| GREEN (1e.2 endpoint implemented) | 524 | 524 | 0 |

---

## Status

**Slice 1e COMPLETE** — 2/2 tasks done. Suite: 524/524 passing.

Next slice: **2a** — Movistar EC SMS adapter (depends on 1b only; can start independently of 1e).
