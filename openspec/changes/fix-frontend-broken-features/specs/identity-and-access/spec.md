# Delta Spec — Identity And Access
# Change: fix-frontend-broken-features
# Base spec: openspec/specs/identity-and-access/spec.md

## Scope of Delta

This delta amends `openspec/specs/identity-and-access/spec.md`. All existing requirements (JWT-Based Authentication, Role-Based Authorization, Permission-Based Sensitive Access, Collections policies, IsoSwitch access) remain unchanged. This delta ADDS the password recovery capability.

---

## Requirement IAM-PR-1: Password Recovery Flow MUST Exist and Be Real

The system SHALL provide a functional password recovery path via two `[AllowAnonymous]` endpoints. The flow must not be simulated, stubbed, or no-op in any production-path code.

### Invariant
- No code path in production builds SHALL set `emailSent = true` or equivalent success state without first receiving a 2xx HTTP response from the backend.
- The `forgot-password.component.ts` SHALL inject `AuthService` and delegate the network call; it MUST NOT contain inline comments like `// Emulación` or equivalent.

---

## Requirement IAM-PR-2: Token Generation MUST Be Cryptographically Strong and Hash-Stored

When `POST /api/auth/forgot-password` triggers token creation, the system SHALL:
1. Generate a 256-bit (32-byte) cryptographically random token using a CSPRNG.
2. Encode the raw token as URL-safe Base64 for transmission to the user.
3. Persist ONLY the SHA-256 hash of the raw token — the plaintext token is never stored.
4. Associate the hash with the requesting user's identity and a UTC expiration timestamp (default: 60 minutes from generation).
5. Return HTTP 202 Accepted with an empty or generic body, regardless of whether the email is registered (enumeration-safe).

### Scenario IAM-PR-2-S1: Token generation produces a non-guessable value
- GIVEN a call to the password reset service
- WHEN a reset token is generated
- THEN the token is at least 256 bits of entropy from a CSPRNG
- AND the persisted value is the SHA-256 hash, not the raw token

### Scenario IAM-PR-2-S2: Enumeration attack is not possible via response differential
- GIVEN email address A is registered and email address B is not registered
- WHEN two separate callers POST each email to `/api/auth/forgot-password`
- THEN both receive HTTP 202 with identical response bodies
- AND response timing does not reveal which email exists

---

## Requirement IAM-PR-3: Token Validation MUST Enforce Expiry, Single-Use, and Password Policy

When `POST /api/auth/reset-password` is called, the system SHALL:
1. Look up the token hash in the persistence store.
2. Reject with HTTP 400 if: the token hash is not found, the expiration timestamp is in the past, or the token has already been marked as used.
3. Validate the new password against the current password policy before applying it.
4. On success: update the credential hash, mark the token as used (or delete it), revoke all existing refresh tokens for the user, and return HTTP 204.

### Scenario IAM-PR-3-S1: Valid token resets password and revokes sessions
- GIVEN a user has a valid, unexpired, unused reset token
- WHEN they POST the correct token and a policy-compliant new password to `/api/auth/reset-password`
- THEN the backend updates the stored credential hash
- AND all existing refresh tokens for that user are revoked
- AND the token is marked used (or deleted)
- AND the response is HTTP 204 No Content

### Scenario IAM-PR-3-S2: Expired token is rejected
- GIVEN a reset token whose expiration timestamp is in the past
- WHEN a caller POSTs it to `/api/auth/reset-password`
- THEN the backend returns HTTP 400 Bad Request
- AND no credential change occurs

### Scenario IAM-PR-3-S3: Reused token is rejected
- GIVEN a reset token that was already successfully used in a prior request
- WHEN a caller POSTs it again to `/api/auth/reset-password`
- THEN the backend returns HTTP 400 Bad Request
- AND the system does not revert or re-apply any credential change

### Scenario IAM-PR-3-S4: Password policy violation is rejected
- GIVEN a valid, unexpired, unused reset token
- WHEN the caller provides a `newPassword` that violates the current password policy (e.g., too short, missing complexity)
- THEN the backend returns HTTP 400 Bad Request
- AND no credential change occurs
- AND the token is NOT consumed (remains valid for a subsequent correct attempt)

---

## Requirement IAM-PR-4: Frontend Components MUST Be Real HTTP Consumers

### Invariant
- `forgot-password.component.ts` SHALL make a real `HttpClient` call via `AuthService.forgotPassword(email)`.
- The success state (email-sent card) SHALL be shown only upon receiving a 2xx response.
- The error state SHALL be shown on non-2xx responses.
- `reset-password.component.ts` (new) SHALL accept a `?token=` query parameter, display a form for new password entry, POST via `AuthService.resetPassword(token, newPassword)`, and show success/error states accordingly.
- The route `/auth/reset-password` SHALL be registered in `app.routes.ts`.

### Scenario IAM-PR-4-S1: Success card is shown only after real 2xx
- GIVEN a user submits their email on the forgot-password page
- WHEN the backend returns 202
- THEN the success card is displayed
- AND no success card appears before the HTTP response arrives

### Scenario IAM-PR-4-S2: Error card is shown on non-2xx
- GIVEN a user submits their email on the forgot-password page
- WHEN the backend returns 4xx or 5xx
- THEN an error card is displayed (not a success card)

### Scenario IAM-PR-4-S3: Reset-password page validates token presence
- GIVEN a user navigates to `/auth/reset-password` without a `?token=` parameter
- THEN the component displays an error indicating the link is invalid or missing

### Scenario IAM-PR-4-S4: Reset-password page succeeds end-to-end
- GIVEN a user navigates to `/auth/reset-password?token=<valid>`
- AND enters a policy-compliant new password
- WHEN they submit the form
- THEN `AuthService.resetPassword(token, newPassword)` is called
- AND on 204 the success state is shown
- AND the user can subsequently log in with the new password

---

## Updated Endpoints Protected Table (addendum)

| Endpoint | Method | Policy |
|----------|--------|--------|
| `/api/auth/forgot-password` | POST | `AllowAnonymous` + rate-limit |
| `/api/auth/reset-password` | POST | `AllowAnonymous` + rate-limit |
