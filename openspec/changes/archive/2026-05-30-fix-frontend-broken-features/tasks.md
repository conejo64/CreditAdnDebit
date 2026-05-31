# Tasks: Fix Frontend Broken Features

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~850–1050 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (Slice 1: installments) → PR 2 (Slice 2: card lifecycle) → PR 3 (Slice 3: password recovery) |
| Delivery strategy | single-pr |
| Chain strategy | size-exception (single PR requires maintainer approval) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: size-exception
400-line budget risk: High

> **Action required.** `single-pr` + ~1000 estimated lines exceeds the 400-line budget. Maintainer must grant `size:exception` before `sdd-apply` starts.

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Installments URL fix + smoke test | PR 1 | base = main; 1-line fix, safest first |
| 2 | Card lifecycle backend + frontend + tests | PR 2 | base = main or PR 1; no external deps |
| 3 | Password recovery backend + frontend + tests | PR 3 | base = PR 2; gates on `real-notification-channels` or ADR-4 stub |

---

## Phase 1: Slice 1 — Installments URL Fix

- [x] 1.1 Fix `features/finance/installment.service.ts`: `baseUrl` from `${environment.apiUrl}/api/billing` → `${environment.apiUrl}/billing`
- [x] 1.2 Write RED test (HttpTestingController): assert `getPlans()` URL has no `/api/api/` and matches `<apiUrl>/billing/installment-plans?accountId=...`
- [x] 1.3 Write RED test: assert `deferPurchase()` POSTs to `<apiUrl>/billing/installment-plans`; make tests GREEN

## Phase 2: Slice 2 — Card Lifecycle Backend

- [x] 2.1 Write RED handler unit tests: `UnblockCard` / `CancelCard` / `ReplaceCard` — success (204/204/201), 404, 409 paths
- [x] 2.2 Write RED integration tests via `CardVaultWebApplicationFactory`: 204/201 authorized, 403 no-policy, 404 missing, 409 conflict
- [x] 2.3 Add `UnblockCardAsync`, `CancelCardAsync`, `ReplaceCardAsync` to `IssuerService`; `ReplaceCardAsync` must be transactional (cancel-old + issue-new in one `SaveChanges`) with bidirectional audit linkage
- [x] 2.4 Add DTOs `CancelCardRequest(string? Reason)`, `ReplaceCardRequest(string? Reason)` in Contracts
- [x] 2.5 Add commands + handlers `UnblockCardCommand`, `CancelCardCommand`, `ReplaceCardCommand` in `Features/Issuer/Commands/` (mirror `BlockCardCommand`)
- [x] 2.6 Append 3 actions to `IssuerController` under `CanOperateIssuer`: `POST cards/{id}/unblock` → 204, `cancel` → 204, `replace` → 201
- [x] 2.7 Make all card lifecycle backend tests GREEN

## Phase 3: Slice 2 — Card Lifecycle Frontend

- [x] 3.1 Write RED service tests: `unblockCard`, `cancelCard`, `replaceCard` — assert method/URL/body via HttpTestingController
- [x] 3.2 Write RED component tests: confirm dialog → service call → success/error state per action
- [x] 3.3 Add `unblockCard(id)`, `cancelCard(id, reason?)`, `replaceCard(id, reason?)` to `card.service.ts`
- [x] 3.4 Fix `card-detail.component.ts`: `Desbloquear` → `unblockCard`; `Cancelar` → `cancelCard`; wire `Reposición` → `replaceCard` + navigate to new card id on 201
- [x] 3.5 Make all card lifecycle frontend tests GREEN

## Phase 4: Slice 3 — Password Recovery Backend

- [x] 4.1 Write RED `PasswordResetService` unit tests: CSPRNG entropy, SHA-256 hash-only storage, expiry reject, single-use reject, policy-violation does NOT consume token
- [x] 4.2 Write RED integration tests: 202 known email, 202 unknown email (enumeration-safe), 204 valid reset, 400 expired, 400 reused
- [x] 4.3 Add `PasswordResetToken` entity + EF config + `DbSet` to `IdentityAppDbContext`
- [x] 4.4 Create first Identity migration 
`AddPasswordResetTokens` in `CardVault.Infrastructure.Identity/Migrations/`; review against live schema (ADR-3 / R-1)
- [x] 4.5 Create `IPasswordResetService` + `PasswordResetService` in `CardVault.Api/Services/` (32-byte CSPRNG, hash-only, 30-min expiry, single-use, enumeration-safe, ADR-4 dispatch stub)
- [x] 4.6 Add `auth_password_reset` rate-limit policy in `Program.cs`; register `PasswordResetService` in DI
- [x] 4.7 Add `ForgotPasswordCommand`, `ResetPasswordCommand` + handlers + DTOs in `Features/Auth/Commands/`
- [x] 4.8 Append `forgot-password` and `reset-password` actions to `AuthController` (`[AllowAnonymous]` + `[EnableRateLimiting("auth_password_reset")]`)
- [x] 4.9 Make all password recovery backend tests GREEN

## Phase 5: Slice 3 — Password Recovery Frontend

- [x] 5.1 Write RED component tests: `forgot-password` HTTP wire-up (success-only-on-2xx, error-on-4xx); `reset-password` token-absence → invalid-link; 204 → success; 400 → error
- [x] 5.2 Add `forgotPassword(email)` and `resetPassword(token, newPassword)` to `auth.service.ts`
- [x] 5.3 Rewrite `sendResetLink()` in `forgot-password.component.ts`: inject `AuthService`, call `forgotPassword`, set `emailSent=true` only on 2xx, error state on non-2xx; remove `// Emulación` stub
- [x] 5.4 Create `reset-password.component.ts` in `features/auth/`: read `?token=` from `ActivatedRoute`; show invalid-link if absent; form (newPassword + confirm); POST via `resetPassword`; success/error states
- [x] 5.5 Register `/auth/reset-password` route in `app.routes.ts` (standalone component)
- [x] 5.6 Make all password recovery frontend tests GREEN; run `dotnet test` + `npm test` — both must be green

## Phase 6: Verification Gap Fixes

These tasks address the 5 gaps discovered during the sdd-verify phase.

- [x] 6.1 **Gap 1 — Base64Url**: Change `PasswordResetService.CreateTokenAsync` to use `WebEncoders.Base64UrlEncode` instead of `Convert.ToBase64String`; add `using Microsoft.AspNetCore.WebUtilities`
- [x] 6.2 **Gap 2 — Timing protection**: Unseal `PasswordResetService`; add `protected virtual void DoTimingWork(string email)` calling `ComputeHash(email)`; call it in the unknown-email early-return branch
- [x] 6.3 **Gap 3a — forgot-password DOM**: Add `<div *ngIf="errorMessage" class="alert alert-danger mb-3">{{ errorMessage }}</div>` to `forgot-password.component.ts` template
- [x] 6.4 **Gap 3b — reset-password DOM**: Add `<div *ngIf="invalidLink" ...>Enlace Inválido</div>` section to `reset-password.component.ts` template; update form guard to `*ngIf="!submitted && !invalidLink"`
- [x] 6.5 **Gap 3c — card-detail newCardId**: Fix `card-detail.component.ts` line 330: `newCard.id` → `newCard.newCardId`; update `card.service.ts` return type to `Observable<{newCardId:string}>`
- [x] 6.6 **Gap 4 — Named audit events**: Add `BlockCardAsync` to `IssuerService` (emits `issuer.card.blocked`); update `UnblockCardAsync` to emit `issuer.card.unblocked`; update `CancelCardAsync` to emit `issuer.card.cancelled`; update `BlockCardCommandHandler` to call `BlockCardAsync`
- [x] 6.7 **Write TDD evidence**: RED tests for all 6 gap-fix tasks written first; GREEN confirmed 284 backend + 93 frontend tests passing
