# Proposal: Fix Frontend Broken Features Falsely Reported As Complete

## Why

Three frontend features are checked off as `✅ Completado` in `funcional/Sprints.md` but are **demonstrably non-functional** in code today. Verified inline:

1. **Sprint 7 — installments page returns 404 on every request.** `frontend/src/app/features/finance/installment.service.ts:55` builds `private baseUrl = \`${environment.apiUrl}/api/billing\`;` while `environment.apiUrl` in `frontend/src/environments/environment.ts:3` is already `http://localhost:5101/api`. Effective URL: `http://localhost:5101/api/api/billing/...` — the duplicated `/api` segment guarantees a 404 against `CardVault.Api`, which mounts billing at `/api/billing`. The deferred-purchase page cannot list or create plans.
2. **Sprint 3 — card lifecycle is only one-quarter implemented.** `frontend/src/app/features/issuer/cards/card.service.ts` exposes only `blockCard()`. Sprint 3 declares "Bloqueo, Desbloqueo, Cancelación, Reposición" as ✅. `IssuerController.cs` confirms backend has `POST /api/issuer/cards/{id}/block` but **no** `unblock`, `cancel`, or `replace` endpoints. Three of four advertised lifecycle actions cannot fire — neither frontend nor backend exists.
3. **Sprint 6 — forgot-password is UI theater.** `frontend/src/app/features/auth/forgot-password.component.ts:84-88` literally sets `this.emailSent = true` with `// Emulación de envío de correo` and zero `HttpClient` interaction. No `AuthService` injection, no API call. `AuthController.cs` has **no** `forgot-password` or `reset-password` endpoint. Users who lose access have no real recovery path.

This matters now because:

- **Credibility damage.** Sprints.md is the single source of truth consumed by stakeholders and auditors. Marking broken features green undermines every other claim in the document.
- **Regulatory readiness.** A card issuer offering self-service flows that silently no-op (password reset) or 404 (installments) cannot pass an operational audit. Cards that can be blocked but never unblocked create irreversible customer impact.
- **Compounding debt.** Each sprint that closes on top of this fake-green baseline buries the gap deeper. The longer it stays advertised as done, the more expensive recovery becomes.

## What Changes

### 1. Installments URL fix

- Correct `installment.service.ts` `baseUrl` to `${environment.apiUrl}/billing` (drop the duplicated `/api` segment).
- Add a smoke test verifying `getPlans()` and `deferPurchase()` issue requests against the documented backend route.

### 2. Card lifecycle completion

- Verify (already confirmed inline) that `IssuerController` only has `block`. Create three new endpoints, each behind `[Authorize(Policy = "CanOperateIssuer")]` and each emitting a domain audit event consistent with the existing `BlockCardCommand` MediatR pattern:
  - `POST /api/issuer/cards/{id}/unblock`
  - `POST /api/issuer/cards/{id}/cancel`
  - `POST /api/issuer/cards/{id}/replace` (issues a replacement card preserving account linkage; returns the new card id)
- Extend `card.service.ts` with `unblockCard(id)`, `cancelCard(id)`, `replaceCard(id, reason)` matching the new endpoints.
- Wire the three new actions into `card-detail.component.ts` with appropriate role-gated buttons and confirmation prompts.
- Cover each backend handler with a unit test on the command, and each frontend action with a service-level test.

### 3. Real forgot-password / reset-password flow

- New `Services/PasswordResetService.cs` (and its `IPasswordResetService` interface) that:
  - Generates a cryptographically random reset token (256-bit URL-safe).
  - Persists token hash + expiration (60 min default) keyed to user id.
  - Invalidates on use and on expiry.
- New `AuthController` endpoints (both `[AllowAnonymous]`, both rate-limited):
  - `POST /api/auth/forgot-password { email }` → generates token, dispatches reset email via `INotificationProvider` (depends on `real-notification-channels` being merged). Returns 202 regardless of whether the email exists (enumeration-safe).
  - `POST /api/auth/reset-password { token, newPassword }` → validates token, applies password policy, updates credential hash, revokes existing refresh tokens, returns 204.
- Inject `AuthService` into `forgot-password.component.ts`; call `POST /api/auth/forgot-password`; show the existing success card only on real 2xx response.
- New `frontend/src/app/features/auth/reset-password.component.ts` bound to a new route `/auth/reset-password?token=...` that validates the token in the form and POSTs to `/api/auth/reset-password`.
- Unit tests on `PasswordResetService` (token generation, hashing, expiry, single-use enforcement) and integration tests on both endpoints. Component tests for both Angular pages (form validation, success/error states, HTTP wire-up).

## Out Of Scope

- Rewriting `funcional/Sprints.md` to reflect the corrected reality — handled separately by `documentation-baseline-reset`.
- Establishing a full frontend smoke-test baseline so this class of regression is caught earlier — handled separately by `frontend-smoke-tests-base`. This change adds only the tests required to lock in the three fixes.
- Notification channel transport itself (SMTP/SMS provider implementation) — owned by `real-notification-channels`, which is a hard prerequisite for the forgot-password slice landing functionally.
- Broader audit of other Sprints.md `✅` items beyond these three — separate audit pass.
- Password policy redesign, MFA-on-reset, or recovery-codes — current policy applies as-is.

## Impacted Areas

- `frontend/src/app/features/finance/installment.service.ts`
- `frontend/src/app/features/issuer/cards/card.service.ts`
- `frontend/src/app/features/issuer/cards/card-detail.component.ts`
- `frontend/src/app/features/issuer/cards/card-list.component.ts` (action menus may need updates)
- `frontend/src/app/features/auth/forgot-password.component.ts`
- `frontend/src/app/features/auth/reset-password.component.ts` (new)
- `frontend/src/app/app.routes.ts` (new reset-password route)
- `frontend/src/app/core/auth.service.ts` (new methods `forgotPassword`, `resetPassword`)
- `backend/services/CardVault/src/CardVault.Api/Controllers/IssuerController.cs` (3 new endpoints)
- `backend/services/CardVault/src/CardVault.Api/Controllers/AuthController.cs` (2 new endpoints)
- `backend/services/CardVault/src/CardVault.Api/Features/Issuer/Commands/` (new `UnblockCardCommand`, `CancelCardCommand`, `ReplaceCardCommand` + handlers)
- `backend/services/CardVault/src/CardVault.Api/Features/Auth/Commands/` (new `ForgotPasswordCommand`, `ResetPasswordCommand` + handlers)
- `backend/services/CardVault/src/CardVault.Api/Services/PasswordResetService.cs` (new) + `IPasswordResetService`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/` (new `PasswordResetToken` entity + EF configuration + migration)
- `backend/services/CardVault/tests/CardVault.Tests/` (handlers and service tests)
- `openspec/specs/identity-and-access/spec.md` (password recovery requirements)
- `openspec/specs/issuer-ledger-billing/spec.md` (card lifecycle requirements)
- `openspec/specs/http-contracts/spec.md` (new endpoint contracts + installment route correction)

## Capabilities Affected

- `http-contracts` — installment route contract correctness + 5 new endpoint contracts (3 issuer + 2 auth).
- `identity-and-access` — password recovery flow becomes a real, tested capability.
- `issuer-ledger-billing` — card lifecycle becomes complete (block / unblock / cancel / replace) with audit on each transition.

This is a **multi-capability** change; spec deltas will span three capability spec files.

## Dependencies

- **Hard prerequisite:** `real-notification-channels` must be merged before the forgot-password slice can be acceptance-tested end-to-end. The email-dispatch path requires a real `INotificationProvider`. If that change is not yet merged when implementation starts, the forgot-password slice ships with the handler emitting the notification request to the configured provider abstraction; integration verification waits for the channel to land.
- Card lifecycle endpoints are independent — no external dependency. Confirmed inline that the three endpoints do not already exist; safe to create.
- Installments URL fix is independent and trivial — no dependency.
- **Architecture dependency:** `kill-or-promote-domain-layers` (Sprint 2) confirms where new backend entities/commands live (Infrastructure.Persistence + Api/Features per the v76 pattern, NOT the empty stub projects).

## Acceptance Criteria

- Installments page in the running frontend loads `getPlans(accountId)` with HTTP 200 against `CardVault.Api`; defer-purchase POST returns the created plan; zero `/api/api/` requests visible in network logs.
- All four card lifecycle buttons (block, unblock, cancel, replace) in `card-detail.component.ts` complete end-to-end against the backend, each produces an audit log entry, and unauthorized roles get 403.
- `POST /api/auth/forgot-password` with a valid registered email generates a reset token, dispatches a notification via the provider, and returns 202; the same endpoint with an unknown email also returns 202 (no enumeration).
- `POST /api/auth/reset-password` with a valid token updates the credential, returns 204, and the user can log in with the new password; expired or reused tokens return 400.
- Frontend `forgot-password.component.ts` makes a real HTTP call; success card appears only on 2xx; error card on non-2xx.
- New `reset-password.component.ts` validates token presence, submits to backend, and shows success/error states.
- Backend tests: handlers for unblock/cancel/replace, `PasswordResetService` unit tests (generation/hash/expiry/single-use), `ForgotPasswordCommand` and `ResetPasswordCommand` handler tests.
- Frontend tests: `installment.service.ts` URL assertion, `card.service.ts` four action methods, `forgot-password.component.ts` HTTP wire-up, `reset-password.component.ts` form + HTTP.
- `dotnet test` and `npm test` both green on the change branch.

## Risks and Mitigations

- **Risk:** PR exceeds the 400-line review budget (5 endpoints + 2 services + 4 components + tests). **Mitigation:** `sdd-tasks` will forecast review workload; expect chained slices: (1) installments fix + tests, (2) card lifecycle backend + frontend + tests, (3) forgot/reset password backend + frontend + tests.
- **Risk:** `real-notification-channels` slips, blocking the forgot-password slice end-to-end verification. **Mitigation:** Land slices (1) and (2) first; gate slice (3) acceptance on the dependency or stub the provider behind a feature flag for staging verification.
- **Risk:** Card replacement semantics (does it revoke the old PAN, keep the token, generate a new token?) are not explicit anywhere. **Mitigation:** `sdd-design` will resolve this; default proposal stance is "cancel old card, issue new card under same account with new PAN token, audit links the two."
- **Risk:** Existing `card-list.component.ts` may render lifecycle buttons that already point at non-existent endpoints. **Mitigation:** `sdd-spec` will enumerate the UI surfaces touched and tests will assert each call.

---

**Inline verification performed during the proposal phase:**
- `frontend/src/environments/environment.ts:3` → `apiUrl: 'http://localhost:5101/api'` (confirmed double `/api` bug)
- `frontend/src/app/features/finance/installment.service.ts:55` → `baseUrl = ${environment.apiUrl}/api/billing` (confirmed)
- `frontend/src/app/features/issuer/cards/card.service.ts` → only `blockCard()` method present (confirmed; note actual path is `features/issuer/cards/`, not `features/cards/` as referenced in original context)
- `backend/.../IssuerController.cs` → `BlockCard` exists, `Unblock`/`Cancel`/`Replace` do NOT exist (confirmed via grep)
- `backend/.../AuthController.cs` → no `forgot-password` or `reset-password` route (confirmed via grep)
- `forgot-password.component.ts:84-88` → `this.emailSent = true` with `// Emulación de envío de correo` comment, no HttpClient (confirmed)
