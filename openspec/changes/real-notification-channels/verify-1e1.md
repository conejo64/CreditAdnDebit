# Verify Report — Slice 1e.1: Webhook Signature Validators
## Change: real-notification-channels
## Date: 2026-06-02
## Branch: feat/notifications-1e1-webhook-validators (commit 52f98a4)
## Verdict: PASS WITH WARNINGS

---

## Test Results

| Suite | Passed | Failed | Skipped | Total |
|-------|--------|--------|---------|-------|
| CardVault.Tests | 503 | 0 | 0 | 503 |
| IsoSwitch.Tests | 37 | 0 | 0 | 37 |
| **Full suite** | **540** | **0** | **0** | **540** |

Webhook validator tests: 27/27 green (26 in WebhookSignatureValidatorTests.cs + 1 contract test in NotificationProviderContractTests.cs).
Baseline was 514; delta is +26.

---

## Findings

### CRITICAL (1)

#### CRIT-1: Future-dated timestamp bypasses replay guard (all three validators)

File: WebhookValidatorHelper.cs:19
Code: return ageSeconds < ReplayWindowSeconds;

ageSeconds = (now - requestTimestamp).TotalSeconds. When requestTimestamp is in the FUTURE,
ageSeconds is NEGATIVE, and negative < 300 is TRUE — so the guard ACCEPTS the request.

An attacker who captures a valid signed request and re-sends it with a timestamp set in the
future (but a valid signature computed over that timestamp) can replay it indefinitely — the
timestamp is always negative-age and always passes the guard.

Fix: Change line 19 to enforce both bounds:
return ageSeconds >= 0 && ageSeconds < ReplayWindowSeconds;

Tests missing: No test case for future-dated timestamp exists for any of the three validators.
Required additions: Validate_FutureDatedTimestamp_ReturnsFalse (x3, one per validator class).

---

### WARNINGS (4)

#### WARN-1: Twilio multi-value form parameter values not sorted (Twilio spec deviation)

File: TwilioWebhookSignatureValidator.cs:96

Twilio docs: for params with multiple values, values must be sorted alphabetically and
concatenated with a comma. StringValues.ToString() returns values in insertion order, not sorted.
Low probability for delivery callbacks but a latent correctness bug. No test for multi-value params.

#### WARN-2: Dummy FixedTimeEquals uses actualBytes length, not expectedBytes length

Files: TwilioWebhookSignatureValidator.cs:69, MovistarWebhookSignatureValidator.cs:71

  CryptographicOperations.FixedTimeEquals(actualBytes, actualBytes);

Dummy runs proportional to actualBytes.Length, not expectedBytes.Length. The expected length is
publicly derivable from the algorithm (HMAC-SHA1 base64 = 28 chars; HMAC-SHA256 hex = 64 chars),
so not exploitable in practice. Recommended fix: use expectedBytes in both arguments of the dummy call.

#### WARN-3: BuildSortedParamString silently swallows all exceptions

File: TwilioWebhookSignatureValidator.cs:98-100

A bare catch returns string.Empty, causing signature to be computed over WebhookUrl + empty.
Not an acceptance bypass (wrong sig is still rejected), but silent exception swallowing makes
debugging impractical in a security-critical path. Catch InvalidOperationException specifically
and return false (not string.Empty) to fail earlier.

#### WARN-4: SendGrid allocates a new ECDsa per request

File: SendGridWebhookSignatureValidator.cs:87-96

New ECDsa object + PEM parse + DER decode on every Validate call. At 600/min rate limit ceiling,
this is 600 key-import operations per minute. Recommended fix: cache the ECDsa instance (or the
imported key bytes) at construction time in the constructor, since IOptions<T> is immutable after startup.

---

### SUGGESTIONS (3)

#### SUGG-1: Contradictory comment in ImportPemPublicKey

File: SendGridWebhookSignatureValidator.cs:113-115

Comment says use ImportFromPem for clarity but code uses manual ImportSubjectPublicKeyInfo.
Misleading to future maintainers. Either switch to ImportFromPem or delete the comment.

#### SUGG-2: Missing Validate_MissingTimestampHeader_ReturnsFalse for Twilio and Movistar

SendGrid has this explicit test. Twilio and Movistar rely on the missing-sig check firing first
when both are absent, leaving the missing-timestamp-only path untested.

#### SUGG-3: Apply-progress test count discrepancy (documentation only)

Apply-progress table says Twilio=8 but the file has 9 Twilio-specific test methods.
Not a code defect; documentation inconsistency only.

---

## Spec / Design Conformance Matrix

| Requirement | Status | Notes |
|-------------|--------|-------|
| IWebhookSignatureValidator interface location | PASS | Correct: INotificationProvider.cs (Slice 1a) |
| Twilio: HMAC-SHA1 over url + sortedParams | PASS / WARN | Single-value correct; WARN-1 for multi-value |
| SendGrid: ECDSA P-256 over timestamp + rawBody | PASS | Correct algorithm, DER-encoded |
| Movistar: HMAC-SHA256 over rawBody, hex-encoded | PASS | Case-insensitive accepted |
| Constant-time comparison on all paths | PASS (WARN-2) | FixedTimeEquals used everywhere |
| Replay guard: forward direction (old timestamps) | PASS | Tested: stale + boundary + just-under |
| Replay guard: future direction (future timestamps) | CRITICAL FAIL | CRIT-1: future age is negative |
| Secrets from IOptions, never in appsettings | PASS | grep confirmed: no provider secrets committed |
| Failure-closed: exceptions result in rejection | PASS (WARN-3) | Silent catch not a bypass |
| ProviderId matches closed set | PASS | twilio / sendgrid / movistar-ec verified |
| 27 new tests, 540 total suite, 0 failures | PASS | Runner: 540 passed, 0 failed |
| Task 1e.1 checkboxes all complete | PASS | tasks.md lines 281-292 all checked |

---

## Security Summary

The implementation is structurally sound. CryptographicOperations.FixedTimeEquals is used on
every signature comparison path. No == or .Equals() on signatures found. No secrets in committed
config files. ECDSA is correctly used for SendGrid (not HMAC). Failure-closed: all exception paths
return false (WARN-3 is a code quality issue, not an acceptance bypass).

The single CRITICAL issue (CRIT-1) is a one-line fix in WebhookValidatorHelper.cs. It must be
resolved before deploying the webhook endpoint (Task 1e.2).

---

## Files Under Review

- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/IWebhookSignatureValidator.cs (in INotificationProvider.cs)
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookSignatureValidator.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookSignatureValidator.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookSignatureValidator.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/WebhookValidatorHelper.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookOptions.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookOptions.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookOptions.cs
- backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Webhooks/WebhookSignatureValidatorTests.cs
