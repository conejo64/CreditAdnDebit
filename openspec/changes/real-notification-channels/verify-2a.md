# Verify Report — Slice 2a (Movistar EC) + Encrypted Destination Fix

**Change**: `real-notification-channels`
**Commits verified**: `8b81e68` (Movistar 2a), `ebb1fd7` (encrypted-destination fix)
**Date**: 2026-06-03
**Verdict**: PASS WITH WARNINGS

---

## Test Suite

| Suite | Passed | Failed | Total |
|---|---|---|---|
| CardVault.Tests | 554 | 0 | 554 |
| IsoSwitch.Tests | 37 | 0 | 37 |
| Notifications-scoped filter | 266 | 0 | 266 |

---

## Findings

### CRITICAL (0)

None.

### WARNINGS (2)

**W1 — AUTH_FAILED classified as PermanentFailure creates silent drop risk during key rotation**
- File: `MovistarEcuadorSmsProvider.cs` line 43 (`PermanentErrorCodes` set) + design §6 table
- The design deliberately classifies `AUTH_FAILED` as `PermanentFailure` (→ DeadLetter, no retry). This is correct per spec. However, if the Movistar API key is rotated while deliveries are in-flight, ALL queued Ecuador SMS deliveries at that moment will DeadLetter with zero retry. There is no alerting hook tied to this specific `LastError` value.
- Matches spec: yes. Operational SLA risk: medium (key rotation window).
- Recommendation: add a Prometheus counter or structured log alert on `AUTH_FAILED` DeadLetter events so on-call can detect and manually re-queue.

**W2 — DestinationCipherB64 lacks MaxLength; migration uses unbounded `text` type**
- File: `CustomerNotificationEntity.cs` line 141; `Migration 20260602231307_AddEncryptedNotificationDestination.cs` line 13-17
- The three companion fields (`DestinationKeyId`, `DestinationNonceB64`, `DestinationTagB64`) all use `varchar(64)`. `DestinationCipherB64` uses PostgreSQL `text` (unbounded). For AES-GCM ciphertext of a phone/email the value fits comfortably in ~256 chars but the inconsistency prevents column-level size enforcement and could mask a larger-than-expected ciphertext silently.
- Risk: low (the actual ciphertext will never be large), but storage inconsistency and missing defensive guard.
- Recommendation: add `[MaxLength(512)]` on the entity property and apply a migration to change the column to `character varying(512)`.

### SUGGESTIONS (2)

**S1 — No test verifying API key never appears in log output**
- File: `MovistarEcuadorSmsProviderTests.cs`
- The `MovistarCapturingHandler` captures request body content. There is no test asserting the captured body does NOT contain the literal API key value. Current code is safe (no log statements write the request body), but a defensive test would prevent future regressions if error-path logging is added.

**S2 — AUTH_FAILED as plain 4xx XML body (non-Fault path) has no triangulation test**
- File: `MovistarEcuadorSmsProviderTests.cs` lines 235-247
- The existing `AUTH_FAILED` test (line 237) wraps the error code in a `soap:Client` fault. The `ClassifySoapResponseAsync` method also handles 4xx non-fault XML responses with an `errorCode` element (lines 191-202). The `AUTH_FAILED` code path via that branch is not directly tested. The code is correct but coverage is incomplete.

---

## Spec Compliance — Design §6 Movistar Classification Table

| Error / Response | Design says | Implemented | Status |
|---|---|---|---|
| `soap:Server` fault | TransientFailure | Lines 238-243: `ClassifySoapFault` → `TransientFailure` | PASS |
| `soap:Client` fault | PermanentFailure | Lines 246-250: `ClassifySoapFault` → `PermanentFailure` | PASS |
| `SYSTEM_BUSY` | TransientFailure | `TransientErrorCodes` set, takes priority over fault code | PASS |
| `THROTTLED` | TransientFailure | `TransientErrorCodes` set | PASS |
| HTTP 429 | TransientFailure | Lines 133-138 (SOAP), 307-308 (REST) | PASS |
| HTTP 5xx | TransientFailure | Lines 141-148 (SOAP), 311-315 (REST) | PASS |
| `INVALID_MSISDN` | PermanentFailure | `PermanentErrorCodes` set | PASS |
| `BLACKLISTED` | PermanentFailure | `PermanentErrorCodes` set | PASS |
| `AUTH_FAILED` | PermanentFailure | `PermanentErrorCodes` set | PASS (see W1) |
| Timeout | TransientFailure | `TaskCanceledException` catch → `TransientFailure("TIMEOUT",...)` | PASS |

---

## Encrypted Destination Fix — PII / Crypto Checks (ebb1fd7)

| Requirement | Check | Status |
|---|---|---|
| Plaintext NEVER persisted unencrypted | `BuildDeliveries` in `NotificationService.cs` encrypts before `SaveChangesAsync` (lines 278-292, 296-310) | PASS |
| Plaintext NEVER logged | No log statement in `NotificationService`, `NotificationDispatcher`, or `MovistarEcuadorSmsProvider` writes `Destination` or `customer.Email`/`customer.Phone` | PASS |
| Decryption only at send time | `GetUnmaskedDestination` called in `DispatchDeliveryAsync` at dispatch time (line 191) | PASS |
| Masked value never reaches provider | Test 4 (`DispatchDelivery_MaskedValueNeverReachesProvider`) asserts `capturedDestination != maskedEmail` | PASS |
| Fail-closed on missing parts | All-null check (lines 515-521) → `DeadLetter("missing-encrypted-destination")` | PASS |
| Fail-closed on corrupt ciphertext | `CryptographicException` catch (lines 531-543) → `null` → `DeadLetter` | PASS |
| Migration Down is valid | `Down()` drops all 4 columns cleanly — safe rollback | PASS |
| Backward compat (pre-1e rows) | All 4 columns nullable in entity and migration; legacy rows handled as fail-closed | PASS |
| Test roundtrip (encrypt → persist → decrypt) | Tests 1a+1b (`CreateNotification_Email_PersistsEncryptedParts_And_DecryptsToOriginalEmail`, SMS variant) | PASS |

---

## SOAP Injection Safety

`BuildSoapEnvelope` (line 361-382) uses `new XElement(ns + elementName, value)` for all user-controlled values (`request.Destination`, `request.RenderedBody`). The XDocument API auto-escapes XML-special characters (`<`, `>`, `&`, `"`, `'`) during serialization. No injection risk.

---

## Secrets Management

| Check | Status |
|---|---|
| `MovistarOptions.cs` has no `ApiKey` property | PASS |
| API key read from env var at call time (not startup) | PASS — `EnvironmentMovistarApiKeyProvider.GetApiKey()` calls `Environment.GetEnvironmentVariable` on each send |
| SOAP path: API key in `SOAPHeader` only, never logged | PASS |
| REST path: API key in JSON request body only, never logged; `ClassifyRestResponseAsync` reads the RESPONSE body, not the request | PASS |
| `appsettings.json` has no secret fields | PASS |

---

## CanHandle Guard

`+593` is Ecuador's unique E.164 country code. `StartsWith("+593", StringComparison.Ordinal)` is correct and cannot accidentally match non-Ecuador numbers. The registry further filters by `Channel == Sms` (line 52 of `NotificationProviderRegistry.cs`) before calling `CanHandle`, so email addresses are never evaluated against this SMS-only check.

---

## Degraded Confirmation

`DegradedConfirmation = true` gate (line 433): the flag is checked inside `BuildAcceptedResult`. Only when the flag is true does `ProviderReportedAt` get set to `DateTimeOffset.UtcNow`. The dispatcher checks `result.ProviderReportedAt.HasValue` (line 297) before setting `DeliveredOn`. The degraded path is logged at `LogWarning` level with explicit `SBS-evidence limitation` wording (line 439-443). Design §13 requirement: PASS.

---

## Tasks Completion vs Code State

| Task | Spec | Code | Status |
|---|---|---|---|
| 2a.1 MovistarEcuadorSmsProvider | [x] | File exists, 459 lines | MATCH |
| 2a.1 MovistarOptions | [x] | File exists | MATCH |
| 2a.1 CanHandle +593 | [x] | Line 64 | MATCH |
| 2a.1 DegradedConfirmation | [x] | Lines 433-452, dispatcher hook lines 297-301 | MATCH |
| 2a.1 Tests 30 cases RED→GREEN | [x] | 30 tests confirmed passing | MATCH |
| 2a.1 Program.cs DI registration | [x] | apply-progress confirms | MATCH |
