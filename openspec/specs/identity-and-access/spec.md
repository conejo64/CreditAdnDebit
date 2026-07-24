# Identity And Access Specification

## Purpose

Define how banking operators and administrators authenticate to CardVault and how access is constrained across protected backend capabilities.

## Requirements

### Requirement: JWT-Based Authentication
The system SHALL authenticate valid users through CardVault and issue bearer-token based access for protected APIs.

CardVault SHALL seed the default administrative user only when the environment is `Development`. CardVault
SHALL NOT carry compiled-in credential fallbacks (e.g. `?? "admin@demo.com"` / `?? "Admin1234!"`); absent
configuration SHALL NOT silently produce a known admin. In non-Development environments, administrative
provisioning is an explicit, controlled operation with no auto-seed.

#### Scenario: Valid login establishes an authenticated session
- WHEN an enabled user submits valid credentials to the CardVault authentication flow
- THEN CardVault returns an access token for subsequent authorized API calls

#### Scenario: Development seeds default operator roles and admin user

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Development`
- AND the identity store is empty
- WHEN CardVault starts
- THEN CardVault seeds the default operator roles and the default administrative user needed for local testing

#### Scenario: Non-Development never auto-seeds a default admin

- GIVEN `ASPNETCORE_ENVIRONMENT` is NOT `Development` (e.g. `Production`, `Staging`)
- AND the identity store is empty
- WHEN CardVault starts
- THEN no administrative user is auto-seeded
- AND no `admin@demo.com` / `Admin1234!` credential is created via any compiled-in fallback
- AND startup does not depend on a hardcoded credential default

### Requirement: Cookie-Based Token Delivery

CardVault SHALL deliver access and refresh tokens to browser clients as `HttpOnly; Secure; SameSite` cookies
and SHALL accept the access token from the cookie in the authentication pipeline, so that token material is
not readable by client-side JavaScript. Token refresh and logout SHALL operate against the cookie model:
refresh reads the refresh-token cookie and reissues cookies; logout clears the token cookies.

The `SameSite` attribute value and whether CORS is configured with `AllowCredentials` for the
`http://localhost:4200` development origin is a **one-way-door decision deferred to design** (see proposal
risk "SameSite/Secure cookie choice conflicts with cross-origin dev setup"). This requirement pins the
observable behavior below and requires the chosen value to keep the Development SPA flow working while never
relaxing `Secure`/`HttpOnly` in `Production`; the specific `SameSite` value (`Strict` vs `Lax` vs `None`) is
selected at design, not invented here.

#### Scenario: Successful login issues HttpOnly Secure token cookies

- GIVEN an enabled user submits valid credentials to the CardVault authentication endpoint
- WHEN authentication succeeds
- THEN the response sets an access-token cookie and a refresh-token cookie
- AND each token cookie carries the `HttpOnly` attribute
- AND each token cookie carries the `Secure` attribute
- AND each token cookie carries a `SameSite` attribute
- AND the token material is NOT returned in a form readable by client-side JavaScript (not in a JS-readable body field relied upon for storage)

#### Scenario: Protected endpoint accepts the token from the cookie

- GIVEN a client holds a valid access-token cookie issued by CardVault
- AND the client sends no `Authorization` header
- WHEN the client requests a protected CardVault endpoint with the cookie
- THEN the request is authenticated and authorized on the basis of the cookie token
- AND the endpoint responds as it would for an equivalently authorized bearer-token caller

#### Scenario: Refresh reissues cookies from the refresh cookie

- GIVEN a client holds a valid refresh-token cookie
- WHEN the client calls the refresh endpoint with the cookie
- THEN CardVault validates the refresh token from the cookie
- AND sets a new access-token cookie (and rotated refresh-token cookie per the existing refresh policy)
- AND does not require the refresh token to be supplied in the request body

#### Scenario: Logout clears the token cookies

- GIVEN an authenticated client with access- and refresh-token cookies
- WHEN the client calls the logout endpoint
- THEN CardVault clears (expires) both token cookies
- AND a subsequent request to a protected endpoint using the cleared cookies is rejected with `401 Unauthorized`

#### Scenario: Production never relaxes HttpOnly or Secure

- GIVEN `ASPNETCORE_ENVIRONMENT` is `Production`
- WHEN CardVault issues token cookies
- THEN every token cookie carries `HttpOnly` and `Secure`
- AND no Development-only relaxation of these attributes applies

### Requirement: Role-Based Authorization
The system SHALL enforce role-based authorization policies for operational banking actions.

#### Scenario: Administrative actions require elevated role membership
- WHEN a caller invokes admin-only capabilities such as vault key rotation or user-role management
- THEN the request is allowed only for users that satisfy the configured administrative policy

#### Scenario: Audit and read-only access is narrower than write access
- WHEN an auditor or read-focused role calls a protected endpoint
- THEN the system grants only the policies explicitly intended for that role

### Requirement: Permission-Based Sensitive Access
The system MUST support claims-based permission checks for especially sensitive operations.

#### Scenario: Detokenization requires explicit permission or admin authority
- WHEN a caller requests a detokenization operation
- THEN the caller must be an administrator or hold the `vault:detokenize` permission claim

### Requirement: Collections Visibility Policy

The system MUST enforce a specific authorization policy (`CanViewCollections`) to restrict access to early delinquency and collections data.

#### Scenario: Operator with collections role accesses the delinquency list

- GIVEN a user assigned a role with the `CanViewCollections` policy
- WHEN the user accesses the delinquency management API
- THEN the request is authorized

#### Scenario: Operator without collections role is denied

- GIVEN a user who is authenticated but lacks the `CanViewCollections` policy
- WHEN the user accesses the delinquency management API
- THEN the request is denied with a 403 Forbidden

### Requirement: Collections Management Authorization

The system SHALL enforce granular authorization for collections write operations, distinguishing between read-only access and mutation capabilities.

#### Scenario: User with collections:manage can mutate

- GIVEN a user with the `collections:manage` permission
- WHEN the user attempts to register a contact attempt or add an internal note
- THEN the system authorizes the request
- AND allows the mutation to proceed

#### Scenario: User with only collections:view cannot mutate

- GIVEN a user with only the `collections:view` permission (read-only)
- WHEN the user attempts to register a contact attempt or add an internal note
- THEN the system rejects the request with HTTP 403 Forbidden
- AND returns an authorization error message

#### Scenario: Admin and Operator roles can manage collections

- GIVEN a user with the `Admin` OR `Operator` role
- AND no explicit `collections:manage` permission (falls back to role)
- WHEN the user attempts a collections write operation
- THEN the system authorizes the request based on role membership
- AND allows the mutation to proceed

#### Scenario: Auditor role excluded from write operations

- GIVEN a user with the `Auditor` role
- AND no explicit `collections:manage` permission
- WHEN the user attempts a collections write operation
- THEN the system rejects the request with HTTP 403 Forbidden
- AND the user can still view collections data (read-only via `collections:view`)

### Requirement: Protected IsoSwitch Operational Access
The system MUST require a valid CardVault-issued bearer token for operational IsoSwitch APIs.

#### Scenario: Switch monitor and audit access require an authenticated role
- WHEN a caller requests switch transactions, ISO logs, or audit records from `IsoSwitch.Api`
- THEN the request is accepted only when the caller presents a valid JWT issued for the platform audience
- AND the caller must satisfy the configured monitor or audit authorization policy

#### Scenario: Switch execution endpoints require operator or admin authority
- WHEN a caller invokes protected ISO transaction execution endpoints such as authorization, capture, reversal, or network management
- THEN `IsoSwitch.Api` rejects anonymous callers
- AND the caller must satisfy the switch-operation authorization policy

### Requirement IAM-PR-1: Password Recovery Flow MUST Exist and Be Real

The system SHALL provide a functional password recovery path via two `[AllowAnonymous]` endpoints. The flow must not be simulated, stubbed, or no-op in any production-path code.

#### Invariant
- No code path in production builds SHALL set `emailSent = true` or equivalent success state without first receiving a 2xx HTTP response from the backend.
- The `forgot-password.component.ts` SHALL inject `AuthService` and delegate the network call; it MUST NOT contain inline comments like `// Emulación` or equivalent.

### Requirement IAM-PR-2: Token Generation MUST Be Cryptographically Strong and Hash-Stored

When `POST /api/auth/forgot-password` triggers token creation, the system SHALL:
1. Generate a 256-bit (32-byte) cryptographically random token using a CSPRNG.
2. Encode the raw token as URL-safe Base64 for transmission to the user.
3. Persist ONLY the SHA-256 hash of the raw token — the plaintext token is never stored.
4. Associate the hash with the requesting user's identity and a UTC expiration timestamp (default: 60 minutes from generation).
5. Return HTTP 202 Accepted with an empty or generic body, regardless of whether the email is registered (enumeration-safe).

#### Scenario IAM-PR-2-S1: Token generation produces a non-guessable value
- GIVEN a call to the password reset service
- WHEN a reset token is generated
- THEN the token is at least 256 bits of entropy from a CSPRNG
- AND the persisted value is the SHA-256 hash, not the raw token

#### Scenario IAM-PR-2-S2: Enumeration attack is not possible via response differential
- GIVEN email address A is registered and email address B is not registered
- WHEN two separate callers POST each email to `/api/auth/forgot-password`
- THEN both receive HTTP 202 with identical response bodies
- AND response timing does not reveal which email exists

### Requirement IAM-PR-3: Token Validation MUST Enforce Expiry, Single-Use, and Password Policy

When `POST /api/auth/reset-password` is called, the system SHALL:
1. Look up the token hash in the persistence store.
2. Reject with HTTP 400 if: the token hash is not found, the expiration timestamp is in the past, or the token has already been marked as used.
3. Validate the new password against the current password policy before applying it.
4. On success: update the credential hash, mark the token as used (or delete it), revoke all existing refresh tokens for the user, and return HTTP 204.

#### Scenario IAM-PR-3-S1: Valid token resets password and revokes sessions
- GIVEN a user has a valid, unexpired, unused reset token
- WHEN they POST the correct token and a policy-compliant new password to `/api/auth/reset-password`
- THEN the backend updates the stored credential hash
- AND all existing refresh tokens for that user are revoked
- AND the token is marked used (or deleted)
- AND the response is HTTP 204 No Content

#### Scenario IAM-PR-3-S2: Expired token is rejected
- GIVEN a reset token whose expiration timestamp is in the past
- WHEN a caller POSTs it to `/api/auth/reset-password`
- THEN the backend returns HTTP 400 Bad Request
- AND no credential change occurs

#### Scenario IAM-PR-3-S3: Reused token is rejected
- GIVEN a reset token that was already successfully used in a prior request
- WHEN a caller POSTs it again to `/api/auth/reset-password`
- THEN the backend returns HTTP 400 Bad Request
- AND the system does not revert or re-apply any credential change

#### Scenario IAM-PR-3-S4: Password policy violation is rejected
- GIVEN a valid, unexpired, unused reset token
- WHEN the caller provides a `newPassword` that violates the current password policy (e.g., too short, missing complexity)
- THEN the backend returns HTTP 400 Bad Request
- AND no credential change occurs
- AND the token is NOT consumed (remains valid for a subsequent correct attempt)

### Requirement IAM-PR-4: Frontend Components MUST Be Real HTTP Consumers

#### Invariant
- `forgot-password.component.ts` SHALL make a real `HttpClient` call via `AuthService.forgotPassword(email)`.
- The success state (email-sent card) SHALL be shown only upon receiving a 2xx response.
- The error state SHALL be shown on non-2xx responses.
- `reset-password.component.ts` (new) SHALL accept a `?token=` query parameter, display a form for new password entry, POST via `AuthService.resetPassword(token, newPassword)`, and show success/error states accordingly.
- The route `/auth/reset-password` SHALL be registered in `app.routes.ts`.

#### Scenario IAM-PR-4-S1: Success card is shown only after real 2xx
- GIVEN a user submits their email on the forgot-password page
- WHEN the backend returns 202
- THEN the success card is displayed
- AND no success card appears before the HTTP response arrives

#### Scenario IAM-PR-4-S2: Error card is shown on non-2xx
- GIVEN a user submits their email on the forgot-password page
- WHEN the backend returns 4xx or 5xx
- THEN an error card is displayed (not a success card)

#### Scenario IAM-PR-4-S3: Reset-password page validates token presence
- GIVEN a user navigates to `/auth/reset-password` without a `?token=` parameter
- THEN the component displays an error indicating the link is invalid or missing

#### Scenario IAM-PR-4-S4: Reset-password page succeeds end-to-end
- GIVEN a user navigates to `/auth/reset-password?token=<valid>`
- AND enters a policy-compliant new password
- WHEN they submit the form
- THEN `AuthService.resetPassword(token, newPassword)` is called
- AND on 204 the success state is shown
- AND the user can subsequently log in with the new password

## Authorization Policies

### CanManageCollections

**Policy**: `CanManageCollections`  
**Authorized**:
- Users with role `Admin`
- Users with role `Operator`
- Users with explicit claim `collections:manage`

**Excluded**:
- `Auditor` role (read-only via `CanViewCollections`)
- Users without role or explicit permission

### CanViewCollections

**Policy**: `CanViewCollections`  
**Authorized**:
- Users with role `Admin`
- Users with role `Operator`
- Users with role `Auditor`
- Users with explicit claim `collections:view`

## Permission Catalog

**New permission**: `collections:manage`  
**Description**: Grants write access to collections operations (contact attempts, notes, and future mutation actions)

## Endpoints Protected

| Endpoint | Method | Policy |
|----------|--------|--------|
| `/api/collections/delinquencies/{id}/contacts` | POST | `CanManageCollections` |
| `/api/collections/delinquencies/{id}/contacts` | GET | `CanViewCollections` |
| `/api/collections/delinquencies/{id}/notes` | POST | `CanManageCollections` |
| `/api/collections/delinquencies/{id}/notes` | GET | `CanViewCollections` |
| `/api/auth/forgot-password` | POST | `AllowAnonymous` + rate-limit |
| `/api/auth/reset-password` | POST | `AllowAnonymous` + rate-limit |
