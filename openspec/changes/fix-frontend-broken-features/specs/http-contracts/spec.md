# Delta Spec — HTTP Contracts
# Change: fix-frontend-broken-features
# Base spec: openspec/specs/http-contracts/spec.md

## Scope of Delta

This delta amends `openspec/specs/http-contracts/spec.md`. All existing contracts remain valid unless explicitly superseded below.

---

## Requirement HC-1: Installment Service Route MUST Resolve Without Duplicated Segment

The `InstallmentService` base URL SHALL be constructed as `${environment.apiUrl}/billing` (not `/api/billing`), because `environment.apiUrl` already contains the `/api` suffix.

### Invariant
- The effective URL for any installment request MUST NOT contain the substring `/api/api/`.
- `getPlans(accountId)` MUST issue a `GET` request to exactly `<apiUrl>/billing/installment-plans?accountId={accountId}` (or the documented sub-path), with zero doubled segments.
- `deferPurchase(payload)` MUST issue a `POST` request to exactly `<apiUrl>/billing/installment-plans`.

### Scenario HC-1-S1: Plans list resolves without 404
- GIVEN `environment.apiUrl` is `http://localhost:5101/api`
- AND `InstallmentService.baseUrl` is set to `${environment.apiUrl}/billing`
- WHEN the frontend calls `getPlans(accountId)`
- THEN the outgoing HTTP request URL is `http://localhost:5101/api/billing/installment-plans?accountId={accountId}` (no `/api/api/`)
- AND the backend returns HTTP 200

### Scenario HC-1-S2: Existing `/api/api/billing/` double-segment is gone
- GIVEN the previous broken configuration where `baseUrl` contained `/api/billing` appended to an `apiUrl` already ending in `/api`
- WHEN the fix is applied
- THEN no request in the installments feature ever reaches the path `/api/api/billing/...`
- AND integration smoke tests assert this URL shape constraint

---

## Requirement HC-2: Five New Endpoint Contracts MUST Be Documented

The following five endpoints SHALL be added to the HTTP contract table. Each entry establishes the canonical method, path, required authorization policy, request body schema, and success response.

> **Implementation note:** The base `http-contracts` spec currently lists `CardService.unblockCard()` → `POST api/issuer/cards/{id}/unblock` as ✅, which contradicts the inline verification that the backend endpoint does NOT exist. Before implementing HC-2.1, RE-VERIFY `IssuerController.cs` for an existing `unblock` route. If it already exists, the gap is only frontend wiring + tests, not a new endpoint.

### HC-2.1 — `POST /api/issuer/cards/{id}/unblock`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/issuer/cards/{id}/unblock` |
| Auth Policy | `CanOperateIssuer` |
| Request Body | none (id in path) |
| Success Response | 204 No Content |
| Error Responses | 401 Unauthorized, 403 Forbidden, 404 Not Found |
| Domain Event | `CardUnblockedEvent` |

### HC-2.2 — `POST /api/issuer/cards/{id}/cancel`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/issuer/cards/{id}/cancel` |
| Auth Policy | `CanOperateIssuer` |
| Request Body | `{ "reason": "string" }` (optional) |
| Success Response | 204 No Content |
| Error Responses | 401 Unauthorized, 403 Forbidden, 404 Not Found |
| Domain Event | `CardCancelledEvent` |

### HC-2.3 — `POST /api/issuer/cards/{id}/replace`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/issuer/cards/{id}/replace` |
| Auth Policy | `CanOperateIssuer` |
| Request Body | `{ "reason": "string" }` (optional) |
| Success Response | 201 Created — body: `{ "newCardId": "uuid" }` |
| Error Responses | 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict (card already cancelled) |
| Domain Event | `CardReplacedEvent` |

### HC-2.4 — `POST /api/auth/forgot-password`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/auth/forgot-password` |
| Auth Policy | `[AllowAnonymous]` |
| Rate Limit | Applied (per-IP, configurable threshold) |
| Request Body | `{ "email": "string" }` |
| Success Response | 202 Accepted — body: `{}` (enumeration-safe: same response for known and unknown emails) |
| Error Responses | 400 Bad Request (malformed body), 429 Too Many Requests |

### HC-2.5 — `POST /api/auth/reset-password`

| Field | Value |
|---|---|
| Method | POST |
| Path | `/api/auth/reset-password` |
| Auth Policy | `[AllowAnonymous]` |
| Rate Limit | Applied (per-IP, configurable threshold) |
| Request Body | `{ "token": "string", "newPassword": "string" }` |
| Success Response | 204 No Content |
| Error Responses | 400 Bad Request (expired token / reused token / password policy violation), 429 Too Many Requests |

### Scenario HC-2-S1: Unblock endpoint is reachable and authorized
- GIVEN a caller with the `CanOperateIssuer` policy
- WHEN they POST to `/api/issuer/cards/{id}/unblock`
- THEN the backend returns 204

### Scenario HC-2-S2: Cancel endpoint returns 204 for authorized caller
- GIVEN a caller with the `CanOperateIssuer` policy
- WHEN they POST to `/api/issuer/cards/{id}/cancel`
- THEN the backend returns 204

### Scenario HC-2-S3: Replace endpoint returns 201 with new card id
- GIVEN a caller with the `CanOperateIssuer` policy
- AND the target card is in a blockable/active state
- WHEN they POST to `/api/issuer/cards/{id}/replace`
- THEN the backend returns 201 with a JSON body containing `newCardId`

### Scenario HC-2-S4: Forgot-password returns 202 for known email
- GIVEN a registered email address
- WHEN an anonymous caller POSTs `{ "email": "<registered>" }` to `/api/auth/forgot-password`
- THEN the backend returns 202 Accepted

### Scenario HC-2-S5: Forgot-password returns 202 for unknown email (enumeration-safe)
- GIVEN an email address that does NOT exist in the system
- WHEN an anonymous caller POSTs `{ "email": "<unknown>" }` to `/api/auth/forgot-password`
- THEN the backend returns 202 Accepted
- AND the response body and timing are indistinguishable from the known-email case

### Scenario HC-2-S6: Reset-password returns 204 on valid token
- GIVEN a valid, unexpired, unused reset token
- WHEN an anonymous caller POSTs `{ "token": "<valid>", "newPassword": "<compliant>" }` to `/api/auth/reset-password`
- THEN the backend returns 204

### Scenario HC-2-S7: Reset-password returns 400 on expired or reused token
- GIVEN a token that is expired OR has already been used
- WHEN an anonymous caller POSTs to `/api/auth/reset-password`
- THEN the backend returns 400 Bad Request
