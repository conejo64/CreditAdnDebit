# Proposal: Secure User Registration via Admin Invitation

## Intent

`POST /api/auth/register` in `CardVault.Api/Controllers/AuthController.cs:22-24` is currently decorated with `[AllowAnonymous]`. Any unauthenticated caller on the internet — or any actor that can reach the API surface — can create user accounts with no identity vetting and no role-assignment governance.

For a banking platform regulated by SBS Ecuador and aiming for PCI-DSS compliance (Requirement 8 — Identify and authenticate access to system components), this is unacceptable:

- **PCI-DSS Req 8.2 / 8.3**: User accounts must be provisioned through a controlled process. Self-registration violates this.
- **SBS access-control expectations**: Access to systems handling cardholder data must follow least-privilege and segregation-of-duties principles. Anonymous account creation breaks both.
- **Segregation of duties**: A user must not be able to manufacture their own credentials to operational systems; account creation must be performed by an authorized role.

This change eliminates anonymous self-registration and replaces it with an admin-driven invitation flow: an authorized administrator (holder of `CanManageUsersRoles`) issues a one-time, time-bounded invitation token. The invitee uses that token to set their initial password and activate the account.

**Success looks like**:
- Anonymous calls to `/api/auth/register` are rejected with `401 Unauthorized`.
- New users can only enter the system through an admin-issued invitation that expires in 48 hours, is single-use, and produces audit events on issuance and acceptance.
- The invitation flow is verifiable end-to-end via automated tests, and the SBS / PCI-DSS audit narrative for user provisioning is closed.

## Scope

### In Scope

- **Backend authorization change**:
  - Remove `[AllowAnonymous]` from `POST /api/auth/register` in `AuthController.cs`.
  - Protect `/register` with `[Authorize(Policy = "CanManageUsersRoles")]` (existing policy — see `Program.cs:143`, backed by `PermissionCatalog.UsersManage = "users:manage"`).

- **Invitation flow (new)**:
  - New domain entity `UserInvitationEntity` with: `Id`, `Email`, `RoleAssignments`, `TokenHash` (SHA-256 of the secret, never store plaintext), `IssuedAtUtc`, `ExpiresAtUtc` (issued + 48h), `IssuedByUserId`, `AcceptedAtUtc?`, `Status` (`Pending` | `Accepted` | `Expired` | `Revoked`).
  - EF Core migration adding `user_invitations` table with indices on `(TokenHash)` and `(Email, Status)`.
  - New service `UserInvitationService` responsible for: generating cryptographically random tokens (32 bytes, base64url), hashing them, persisting the invitation, marking single-use on acceptance, and enforcing TTL/state transitions.
  - New endpoint `POST /api/auth/invitations` (admin-only, `CanManageUsersRoles`): accepts `{ email, roles[] }`, returns the **plaintext token once** to the caller (for delivery via email channel) plus the invitation metadata.
  - New endpoint `POST /api/auth/accept-invite` (anonymous, but token-gated): accepts `{ token, password }`. Validates the hash, expiry, and `Pending` status; on success creates the user, assigns the pre-declared roles, marks the invitation `Accepted`, and emits an audit event. On expiry returns `410 Gone`; on invalid token returns `400 Bad Request`; on already-used token returns `409 Conflict`.
  - Refactor existing `RegisterUserCommand` so it can be reused internally by `accept-invite` without re-exposing the anonymous controller path.

- **Audit and observability**:
  - Audit event `UserInvitationIssued` (issuer, email, roles, expiry).
  - Audit event `UserInvitationAccepted` (invitee, originating issuer, accepted timestamp).
  - Audit event `UserInvitationRejected` (token-not-found / expired / already-used) for security forensics.

- **Email notification hand-off**:
  - When an invitation is issued, publish an outbound notification request containing the recipient email and the plaintext token to the notification subsystem. **Delivery** is owned by the `real-notification-channels` change (separate proposal); this change publishes the request and persists the invitation regardless of delivery outcome (the admin always gets the token back in the API response as a fallback so they can deliver manually if email is unavailable).

- **Tests**:
  - Unit tests for `UserInvitationService` covering token generation, hashing, TTL enforcement, single-use semantics, and state transitions.
  - Integration tests under `tests/CardVault.Tests/Features/Auth/` for the full flow: anonymous `/register` returns `401`, admin issues invite, invitee accepts with valid token, expired token returns `410`, reused token returns `409`, malformed token returns `400`.
  - Authorization-policy test: caller without `CanManageUsersRoles` issuing an invite gets `403`.

### Out of Scope

- **SSO / federated identity** (Azure AD, OIDC providers) — separate strategic decision.
- **MFA enrollment improvements** — MFA enable/verify endpoints stay as they are; tightening their access posture is a separate concern.
- **Password policy revamp** (length, complexity, breach-list checks) — separate change; this proposal accepts whatever policy is currently enforced by the registration command handler.
- **Email transport / SMTP / provider integration** — owned by `real-notification-channels`. This change only publishes the notification request.
- **Bulk invitation / CSV import** — only single-invite-per-call in v1.
- **Invitation revocation UI** — backend should expose `DELETE /api/auth/invitations/{id}` for completeness, but a frontend admin screen is deferred.
- **Frontend admin UI for issuing invites** — backend-first; UI follows in a sibling change once the API is stable.

## Capabilities

### Modified Capabilities

- **`identity-and-access`**: Add SHALL requirements covering:
  - User creation is restricted to callers holding `CanManageUsersRoles`.
  - Invitation tokens MUST be single-use, MUST expire within 48 hours of issuance, MUST be stored only as a cryptographic hash, and MUST emit audit events on issuance and on every acceptance attempt (success or failure).
  - Anonymous `/register` MUST be removed from the public surface.

No new capability is created; this is a delta to the existing `identity-and-access` spec at `openspec/specs/identity-and-access/spec.md`.

## Approach

We follow the patterns already established in CardVault:

1. **MediatR commands/queries** for the new flows (`IssueUserInvitationCommand`, `AcceptUserInvitationCommand`, `RevokeUserInvitationCommand`), keeping the controller thin.
2. **Existing authorization policy** (`CanManageUsersRoles`) instead of inventing a new one — the permission `users:manage` already implies the right to create users; we are just enforcing it on `/register` now and on the new `/invitations` endpoint.
3. **Hash-only token storage** following the same posture as password storage. The plaintext token is returned exactly once at issuance (to the admin) and travels via the notification channel to the invitee; the database only ever holds `SHA-256(token)`. This means a database compromise does not yield usable invitation tokens.
4. **Reuse `RegisterUserCommand`** internally from the `AcceptUserInvitation` handler so we do not duplicate user-creation logic. The command itself stops being callable from the HTTP surface anonymously; it remains an internal application primitive.
5. **Decouple from email delivery**: the invitation persists and the API returns the plaintext token to the admin regardless of whether the email notification publish succeeds. This avoids a hard runtime dependency on `real-notification-channels` and keeps the admin unblocked if email is temporarily down. Email delivery is a best-effort enhancement that the admin can fall back from.

Rationale for **48-hour TTL**: aligns with common banking and PCI guidance for transient credentials (short enough to limit window of misuse, long enough to accommodate weekends and time-zone differences for new operators). We will make this configurable via `appsettings.json` so security policy can tighten it later without code changes, but the default and contractual SHALL is 48 hours.

Rationale for **single-use**: a multi-use token defeats the purpose of provisioning a specific user. Once accepted, the invitation transitions to `Accepted` and the token hash is no longer matchable.

Rationale for **returning plaintext token in admin response**: the admin must be able to deliver the token through an alternative channel if email is unavailable (operational continuity). This is acceptable because the admin is already trusted (`CanManageUsersRoles`) and the token only enables creating the specific invited account.

> **Note (architecture path):** The proposal references `CardVault.Domain/` and `CardVault.Application/` projects, which are currently empty stubs. The `kill-or-promote-domain-layers` change (Sprint 2) decides their fate. If that change lands as "modular monolith — entities in Infrastructure.Persistence, handlers in Api/Features" (the v76 pattern), the `sdd-design` phase MUST relocate `UserInvitationEntity` to `CardVault.Infrastructure.Persistence/Auth/` and the commands/service to `CardVault.Api/Features/Auth/`. Do not create files in the stub projects.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/services/CardVault/src/CardVault.Api/Controllers/AuthController.cs` | Modified | Remove `[AllowAnonymous]` from `/register`. Add `[Authorize(Policy = "CanManageUsersRoles")]`. Add `POST /api/auth/invitations` and `POST /api/auth/accept-invite` endpoints. Add `DELETE /api/auth/invitations/{id}` (admin) for revocation. |
| `UserInvitationEntity` (location resolved by `kill-or-promote-domain-layers`) | New | Domain entity: token hash, email, roles, issuance/expiry/acceptance state. |
| `UserInvitationService` | New | Token generation, hashing, lifecycle/state transitions. |
| `IssueUserInvitationCommand` / `AcceptUserInvitationCommand` / `RevokeUserInvitationCommand` | New | MediatR commands + handlers. |
| `CardVault.Infrastructure.Persistence` | Modified | EF Core `DbSet<UserInvitationEntity>` + migration `AddUserInvitations`. |
| `UserInvitationIssuedNotification` | New | Outbound notification payload (recipient email + plaintext token). Consumed by `real-notification-channels` once that change lands. |
| Audit event types | Modified | Add `UserInvitationIssued`, `UserInvitationAccepted`, `UserInvitationRejected`. |
| `backend/services/CardVault/tests/CardVault.Tests/Features/Auth/UserInvitationServiceTests.cs` | New | Unit tests for token hashing, TTL, single-use. |
| `backend/services/CardVault/tests/CardVault.Tests/Features/Auth/InvitationFlowIntegrationTests.cs` | New | E2E happy path + 401/410/409/400/403 negative paths. |
| `openspec/specs/identity-and-access/spec.md` | Modified (delta in `sdd-spec` phase) | Add SHALL requirements for invitation-based provisioning. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Existing automated provisioning scripts or seed code rely on anonymous `/register` and break in CI/local-dev | Medium | Audit `Program.cs` seeding paths and dev scripts during `sdd-spec`. Seeders should use internal application primitives directly, not the HTTP endpoint. Add a migration note in tasks. |
| Notification subsystem (`real-notification-channels`) is not yet deployed, so invitees receive no email | High (timing) | Admin gets the plaintext token back in the issuance response and can deliver it manually as a fallback. Invitation flow is fully functional without email delivery. |
| Token leakage via logs | Medium | Mark request/response models so logging filters strip the `token` field; integration test asserts the token does not appear in serialized log output. |
| Replay attacks against `accept-invite` | Low | Single-use enforcement: state transitions to `Accepted` atomically inside a transaction; concurrent acceptance of the same token results in one success and one `409 Conflict`. |
| TTL misconfiguration (e.g., admin sets effective TTL to 30 days via config) | Low | Hard upper bound enforced in code: `expiry > 72h` is rejected at the service layer regardless of config. Configurable lower, never higher. |
| Token entropy too low | Very Low | Use `RandomNumberGenerator.Create()` with 32 bytes (256 bits); same posture as JWT signing material. |
| Migration deploys before code, leaving `user_invitations` table empty and orphaned | Low | Standard EF Core forward-only deployment; rollback plan covers removal. |

## Rollback Plan

- **Code**:
  - Re-add `[AllowAnonymous]` to `/register` in `AuthController.cs` (temporary mitigation only — document as an open security finding immediately).
  - Remove `/invitations` and `/accept-invite` endpoints.
  - Remove `UserInvitationService`, command handlers, and notification publisher.
- **Database**:
  - `dotnet ef migrations remove` for `AddUserInvitations`.
  - Drop `user_invitations` table if migration already applied to a higher environment (script provided in tasks).
- **Spec**:
  - Revert delta to `openspec/specs/identity-and-access/spec.md`.

The proper rollback for a security-tightening change is to fix forward, not loosen access again. If a rollback is required, it must be accompanied by a security incident ticket capturing the regression.

## Dependencies

- **Hard dependency**: Existing `CanManageUsersRoles` policy and `users:manage` permission (already present at `Program.cs:143` and `PermissionCatalog.cs:28`).
- **Hard dependency**: Existing `RegisterUserCommand` and the user/role identity stack (reused as an internal primitive).
- **Soft dependency**: `real-notification-channels` change (separate proposal) for actual email delivery. This proposal does NOT block on it — the API returns the token to the admin as a fallback delivery path.
- **Architecture dependency**: `kill-or-promote-domain-layers` (Sprint 2) determines where new entities/services live.
- **No dependency** on frontend changes; admin UI is deferred.

## Success Criteria

- [ ] Anonymous `POST /api/auth/register` returns `401 Unauthorized`.
- [ ] Authenticated caller without `CanManageUsersRoles` calling `POST /api/auth/register` or `POST /api/auth/invitations` returns `403 Forbidden`.
- [ ] Admin with `CanManageUsersRoles` can issue an invitation; response contains a plaintext token (returned exactly once) and invitation metadata.
- [ ] Invitee can call `POST /api/auth/accept-invite` with the plaintext token and a new password, and receives a created user record + login-ready state.
- [ ] Same token presented a second time returns `409 Conflict`.
- [ ] Token presented after 48 hours returns `410 Gone`.
- [ ] Malformed / unknown token returns `400 Bad Request`.
- [ ] `UserInvitationIssued` audit event is emitted on every successful issuance (with issuer, invitee email, roles, expiry).
- [ ] `UserInvitationAccepted` audit event is emitted on every successful acceptance (with invitee, originating issuer, timestamp).
- [ ] `UserInvitationRejected` audit event is emitted on every failed acceptance (with reason: expired, already-used, unknown).
- [ ] Database stores only `SHA-256(token)` — verified by inspecting `user_invitations` table in integration test.
- [ ] Plaintext tokens do not appear in application logs — verified by integration test asserting log output.
- [ ] All new code paths covered by unit + integration tests under `tests/CardVault.Tests/Features/Auth/`.
- [ ] `openspec/specs/identity-and-access/spec.md` updated with new SHALL requirements (delivered by `sdd-spec`).
