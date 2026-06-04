# Verify Report (Closing) -- Slice 1e.1: Webhook Signature Validators
## Change: real-notification-channels
## Date: 2026-06-03
## Branch: feat/notifications-1e1-webhook-validators (hardening commits: 6e9913c, 04c6746, f89d51a)
## Verdict: PASS

---

## Test Results

| Suite | Passed | Failed | Skipped | Total |
|-------|--------|--------|---------|-------|
| CardVault.Tests | 524 | 0 | 0 | 524 |
| IsoSwitch.Tests | 37 | 0 | 0 | 37 |
| Full suite | 561 | 0 | 0 | 561 |

Webhook validator tests: 33/33 green
(32 in WebhookSignatureValidatorTests.cs + 1 in NotificationProviderContractTests.cs)

Post-hardening baseline was 546 (apply-progress-1e1.md). Current 561 reflects legitimate tests
from subsequent slices (1e endpoint + Slice 2a Movistar SMS) -- not regressions.

---

## Prior Findings Status

### CRIT-1 -- CLOSED
Future-dated timestamp bypasses replay guard (WebhookValidatorHelper.cs:20)

Fix: return ageSeconds >= 0 && ageSeconds < ReplayWindowSeconds;

Tests confirming closure (all three pass; each would fail without the fix):
- Validate_FutureDatedTimestamp_ReturnsFalse (Twilio)   line 213
- Validate_FutureDatedTimestamp_ReturnsFalse (SendGrid) line 463
- Validate_FutureDatedTimestamp_ReturnsFalse (Movistar) line 641

### WARN-1 -- CLOSED
Twilio multi-value params not sorted alphabetically

Fix at TwilioWebhookSignatureValidator.cs:110:
  .Select(kv => kv.Key + string.Concat(kv.Value.OrderBy(v => v, StringComparer.Ordinal)))

Test: Validate_MultiValueParamSortedAlphabetically_ReturnsTrue (line 247) -- PASS
Single-value regression check: OrderBy on a single-element is a no-op; existing single-value
tests pass unchanged. No regression.

### WARN-2 -- CLOSED
Dummy FixedTimeEquals used actualBytes, not expectedBytes

Fix confirmed:
- TwilioWebhookSignatureValidator.cs:83:   CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes)
- MovistarWebhookSignatureValidator.cs:75: CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes)
Timing now independent of attacker-supplied input length.

### WARN-3 -- CLOSED
BuildSortedParamString bare catch silently swallowed exceptions

Fix at TwilioWebhookSignatureValidator.cs:64-68:
  catch (InvalidOperationException) { return false; }
No bare catch {}. grep for catch{} returned 0 matches. All exception paths are fail-closed.

### WARN-4 -- CLOSED
SendGrid allocated new ECDsa per request

Fix: ECParameters cached as readonly _publicKeyParameters at construction
(SendGridWebhookSignatureValidator.cs:35, 50-52).
Per-call Validate() uses ECDsa.Create(_publicKeyParameters) -- struct copy, no PEM/DER work.
Thread-safety confirmed (see adversarial sweep).

### SUGG-1 -- CLOSED
Contradictory ImportFromPem comment removed.
SendGridWebhookSignatureValidator.cs:120-122 now correctly describes ImportSubjectPublicKeyInfo.

### SUGG-2 -- CLOSED
Validate_MissingTimestampHeader_ReturnsFalse added for Twilio (line 229) and Movistar (line 656).
Both PASS.

### SUGG-3 -- NOTED (documentation artifact, no code change needed)

---

## Adversarial Sweep -- New Issues Found

CRITICAL: 0
WARNINGS: 0
SUGGESTIONS: 1

### SUGG-A (new): SendGrid broad catch (Exception) -- SendGridWebhookSignatureValidator.cs:113
  catch (Exception) { return false; }
Comes after catch (CryptographicException). Safety net for any unexpected exception from
ECDsa.Create or VerifyData. Fail-closed. Not a security defect. Low priority: document as
intentional or narrow to known specific exception types.

---

## Adversarial Sweep -- Detailed Analysis

WARN-4 thread-safety: SOUND
_publicKeyParameters is an ECParameters struct (value type), readonly. ECDsa.Create(ECParameters)
creates a new independent instance per call (struct copy). using block disposes per-call instance.
No shared mutable state between concurrent Validate() calls.

WARN-1 sort -- single-value regression: NONE
OrderBy on a single-element StringValues is a no-op; string.Concat(singleElement) == element.
Validate_ValidSignatureNoParams_ReturnsTrue and Validate_ValidSignatureWithSortedParams_ReturnsTrue
both pass unchanged.

WARN-3 exception -- fail-open risk: NONE
All Twilio Validate() exit paths: missing sig -> false, missing/invalid ts -> false,
out-of-window -> false, InvalidOperationException -> false, length mismatch -> false,
FixedTimeEquals mismatch -> false. No path returns true on any exception condition.

CRIT-1 boundary correctness: CONFIRMED
- 0.0s (exactly-now):  0.0>=0 && 0.0<300 -> accept. Correct.
- -0.001s (future):    -0.001>=0 -> reject. Correct.
- 300.0s (boundary):   300.0<300 is false -> reject. Correct (boundary excluded per spec).
- 299.9s:              299.9>=0 && 299.9<300 -> accept. Correct.
TotalSeconds is double; no integer overflow possible.

Constant-time comparison audit: CLEAN
grep for == and .Equals() on sig/hash/hmac/hex/expected/actual in all webhook validator files:
0 matches. Every signature comparison uses CryptographicOperations.FixedTimeEquals.

---

## Spec / Design Conformance Matrix

| Requirement | Status | Notes |
|-------------|--------|-------|
| IWebhookSignatureValidator location | PASS | INotificationProvider.cs (Slice 1a) |
| Twilio: HMAC-SHA1 over url + sortedParams | PASS | Single-value and multi-value correct |
| SendGrid: ECDSA P-256 over timestamp + rawBody | PASS | ECParameters cached; fresh ECDsa per call |
| Movistar: HMAC-SHA256 over rawBody, hex-encoded | PASS | Case-insensitive comparison |
| Constant-time comparison on all paths | PASS | FixedTimeEquals; no == on signatures |
| Replay guard: old timestamps | PASS | Stale + boundary + just-under tested |
| Replay guard: future timestamps | PASS | CRIT-1 closed; 3 explicit tests pass |
| Secrets from IOptions only | PASS | No provider secrets in committed config |
| Failure-closed on all exception paths | PASS | All catch paths return false |
| ProviderId matches closed set | PASS | twilio / sendgrid / movistar-ec |
| 33 webhook tests, 0 failures | PASS | 33/33 green |
| Task 1e.1 checkboxes complete | PASS | tasks.md Task 1e.1 fully checked off |

---

## Security Summary

All six prior findings are confirmed closed by code inspection and passing tests. No new security
defects were found. The implementation is production-ready pending Task 1e.2 (DI registration +
endpoint). One low-priority suggestion (SUGG-A) noted for the broad catch (Exception) in
SendGridWebhookSignatureValidator -- fail-closed, non-exploitable.

---

## Files Under Review

- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/WebhookValidatorHelper.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/TwilioWebhookSignatureValidator.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/SendGridWebhookSignatureValidator.cs
- backend/services/CardVault/src/CardVault.Api/Services/Notifications/Webhooks/MovistarWebhookSignatureValidator.cs
- backend/services/CardVault/tests/CardVault.Tests/Features/Notifications/Webhooks/WebhookSignatureValidatorTests.cs
