# Delta Spec — Secure User Registration via Admin Invitation
# Capability: identity-and-access
# Change: secure-user-registration
# Base spec: openspec/specs/identity-and-access/spec.md

This document records ONLY what changes. Apply it as a delta on top of the base spec.
Unchanged requirements are not repeated here.

---

## MODIFIED Requirements

### Requirement: User Account Provisioning (replaces implicit open-registration posture)

The system SHALL restrict user account creation to callers holding the `CanManageUsersRoles` permission. Anonymous self-registration SHALL NOT be permitted.

#### Scenario: Anonymous POST /api/auth/register is rejected

- GIVEN an unauthenticated caller (no bearer token)
- WHEN the caller sends `POST /api/auth/register` with any payload
- THEN the system returns `401 Unauthorized`
- AND no user account is created

#### Scenario: Authenticated caller without CanManageUsersRoles is rejected

- GIVEN a caller authenticated as `Auditor` or any role without `users:manage`
- WHEN the caller sends `POST /api/auth/register`
- THEN the system returns `403 Forbidden`
- AND no user account is created

#### Scenario: Caller with CanManageUsersRoles may create users only via the invitation flow

- GIVEN a caller with the `CanManageUsersRoles` policy satisfied
- WHEN the caller attempts to provision a new user
- THEN the system accepts only the invitation-issuance path (`POST /api/auth/invitations`)
- AND direct `POST /api/auth/register` remains unavailable on the public HTTP surface

---

## ADDED Requirements

### Requirement: Invitation Token Issuance

The system SHALL allow callers holding `CanManageUsersRoles` to issue invitation tokens that enable a specific email address to activate an account with pre-declared roles.

#### Scenario: Admin issues an invitation successfully

- GIVEN a caller with the `CanManageUsersRoles` policy
- WHEN the caller sends `POST /api/auth/invitations` with `{ "email": "<address>", "roles": ["<role>"] }`
- THEN the system generates a cryptographically random token of at least 256 bits of entropy
- AND stores only `SHA-256(token)` in the `user_invitations` table (never the plaintext)
- AND returns the plaintext token exactly once in the response alongside invitation metadata
- AND emits the `UserInvitationIssued` audit event recording issuer identity, invitee email, pre-declared roles, and expiry timestamp
- AND the invitation status is `Pending`

#### Scenario: Caller without CanManageUsersRoles cannot issue invitations

- GIVEN a caller authenticated but without `users:manage`
- WHEN the caller sends `POST /api/auth/invitations`
- THEN the system returns `403 Forbidden`
- AND no invitation is created
- AND no audit event is emitted

### Requirement: Invitation Token Constraints

Invitation tokens SHALL satisfy ALL of the following constraints simultaneously:

1. **Single-use**: once a token transitions to `Accepted`, any further presentation returns `409 Conflict`.
2. **Time-bounded**: tokens expire 48 hours after issuance (configurable lower, never higher). The system SHALL enforce a hard ceiling of 72 hours in code; any configuration value above 72 hours MUST be rejected at application startup.
3. **Hash-only storage**: the `user_invitations` table MUST store `SHA-256(token)` exclusively. No migration, log line, or response body MUST contain the plaintext token except the single issuance response to the admin caller.
4. **High entropy**: tokens MUST be generated with `System.Security.Cryptography.RandomNumberGenerator` using at least 32 bytes (256 bits) of raw random material.

#### Scenario: Plaintext token does not appear in application logs

- GIVEN any invitation lifecycle operation (issue, accept, reject)
- WHEN log output is captured for that request
- THEN the plaintext token value MUST NOT appear in any structured or unstructured log line
- AND the `TokenHash` column value MUST NOT be labelled or indexed in a way that enables reverse lookup via logs

#### Scenario: Token entropy meets the 256-bit floor

- GIVEN the system generates a new invitation token
- WHEN the raw random bytes are inspected (unit test)
- THEN the byte array has length >= 32
- AND was produced by `RandomNumberGenerator.GetBytes(32)` or equivalent CSPRNG

#### Scenario: TTL hard ceiling is enforced

- GIVEN an operator sets `InvitationExpiryHours` to `73` in `appsettings.json`
- WHEN the application starts
- THEN startup MUST fail or the configured value MUST be overridden with `72`
- AND the resulting invitation expiry is at most `IssuedAtUtc + 72h`

#### Scenario: Default TTL is 48 hours

- GIVEN no custom `InvitationExpiryHours` configuration is present
- WHEN an invitation is issued
- THEN `ExpiresAtUtc` equals `IssuedAtUtc + 48 hours` (UTC, second precision)

### Requirement: Invitation Acceptance Flow

The system SHALL allow an invitee to activate their account by presenting the plaintext token and setting an initial password. The flow SHALL enforce token validity before creating any user record.

#### Scenario: Valid token accepted — user account created with pre-declared roles

- GIVEN a `Pending` invitation with `ExpiresAtUtc` in the future
- AND the invitee presents the correct plaintext token via `POST /api/auth/accept-invite` with `{ "token": "<value>", "password": "<new-password>" }`
- WHEN the system computes `SHA-256(token)` and matches it against `user_invitations.TokenHash`
- THEN the system creates a user account for the invitation email
- AND assigns the roles pre-declared at invitation issuance
- AND marks the invitation status as `Accepted` with the current `AcceptedAtUtc` timestamp
- AND emits the `UserInvitationAccepted` audit event recording invitee identity, originating issuer, and acceptance timestamp
- AND returns `200 OK` (or `201 Created`) with a login-ready response

#### Scenario: Expired token returns 410 Gone

- GIVEN a `Pending` invitation whose `ExpiresAtUtc` is in the past
- WHEN the invitee presents the token via `POST /api/auth/accept-invite`
- THEN the system returns `410 Gone`
- AND no user account is created
- AND emits the `UserInvitationRejected` audit event with reason `Expired`

#### Scenario: Already-used token returns 409 Conflict

- GIVEN an invitation whose status is `Accepted`
- WHEN any caller presents the same token via `POST /api/auth/accept-invite`
- THEN the system returns `409 Conflict`
- AND no duplicate user account is created
- AND emits the `UserInvitationRejected` audit event with reason `AlreadyUsed`

#### Scenario: Concurrent acceptance — exactly one succeeds

- GIVEN two simultaneous requests to `POST /api/auth/accept-invite` with the same valid token
- WHEN both requests are processed
- THEN exactly one returns a success response
- AND the other returns `409 Conflict`
- AND the `user_invitations` row is transitioned to `Accepted` exactly once (enforced by DB-level atomic update or optimistic concurrency)

#### Scenario: Malformed or unknown token returns 400 Bad Request

- GIVEN a caller sends a token value that does not match any `TokenHash` in `user_invitations`
  OR the token value is syntactically invalid (empty, not base64url, incorrect length)
- WHEN the request is processed
- THEN the system returns `400 Bad Request`
- AND emits the `UserInvitationRejected` audit event with reason `InvalidToken`
- AND the error response MUST NOT reveal whether the token was structurally valid or simply unknown (constant-time comparison MUST be used to prevent timing side-channels)

### Requirement: Invitation Audit Trail

The system SHALL emit a structured audit event for every significant lifecycle transition of an invitation. Audit events SHALL be durable — they MUST NOT be discarded if an audit store write fails (dead-letter or fallback behavior required).

#### Scenario: UserInvitationIssued event contains required fields

- GIVEN a successful `POST /api/auth/invitations`
- THEN the emitted `UserInvitationIssued` audit event MUST include:
  - `IssuedByUserId` — identity of the admin caller
  - `InviteeEmail` — target email address
  - `PreDeclaredRoles` — array of role names
  - `ExpiresAtUtc` — computed expiry
  - `InvitationId` — opaque identifier (UUID) for correlation

#### Scenario: UserInvitationAccepted event contains required fields

- GIVEN a successful `POST /api/auth/accept-invite`
- THEN the emitted `UserInvitationAccepted` audit event MUST include:
  - `InvitationId` — correlates to the original `UserInvitationIssued` event
  - `AcceptedByEmail` — the invitee's email
  - `OriginalIssuerId` — the admin who originally issued the invitation
  - `AcceptedAtUtc` — UTC timestamp of acceptance

#### Scenario: UserInvitationRejected event contains required fields

- GIVEN a failed acceptance attempt
- THEN the emitted `UserInvitationRejected` audit event MUST include:
  - `InvitationId` (when resolvable) OR a masked token reference (when the token did not match any record)
  - `Reason` — one of: `Expired`, `AlreadyUsed`, `InvalidToken`
  - `AttemptedAtUtc` — UTC timestamp of the attempt
  - `CallerIp` — remote IP of the accept-invite caller (for forensics)

---

## MODIFIED: Authorization Policies (addendum to base spec table)

### CanManageUsersRoles (extended scope)

**Policy**: `CanManageUsersRoles`
**Authorized**:
- Users with role `Admin`
- Users with explicit claim `users:manage`

**Scope expanded by this change**:
- `POST /api/auth/register` — now requires this policy (was `[AllowAnonymous]`)
- `POST /api/auth/invitations` — new endpoint, requires this policy
- `DELETE /api/auth/invitations/{id}` — new endpoint (revocation), requires this policy

**Excluded**:
- `Operator` role (unless explicitly granted `users:manage`)
- `Auditor` role
- Unauthenticated callers

---

## MODIFIED: Endpoints Protected (addendum to base spec table)

| Endpoint | Method | Policy | Notes |
|---|---|---|---|
| `/api/auth/register` | POST | `CanManageUsersRoles` | Was `[AllowAnonymous]` — MODIFIED |
| `/api/auth/invitations` | POST | `CanManageUsersRoles` | New — issue invitation |
| `/api/auth/invitations/{id}` | DELETE | `CanManageUsersRoles` | New — revoke pending invitation |
| `/api/auth/accept-invite` | POST | Anonymous (token-gated) | New — invitee activates account |

---

## Permission Catalog (addendum)

No new permission string is introduced. `users:manage` (already registered as `CanManageUsersRoles` in `PermissionCatalog.cs`) now governs both the modified `/register` endpoint and the new invitation endpoints.

---

## Out-of-Scope Confirmations

The following are explicitly NOT specified in this delta and SHALL remain unchanged from the base spec:

- MFA enrollment and verification endpoints — access posture unchanged.
- Password policy (complexity, breach-list) — unchanged; acceptance flow reuses existing `RegisterUserCommand` password validation.
- Email transport / SMTP — this delta specifies only that an outbound notification request is published; delivery is owned by `real-notification-channels`.
- Frontend admin UI for invitation management — deferred.
- SSO / federated identity — separate decision.
