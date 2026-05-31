# Apply Progress: fix-frontend-broken-features

**Mode**: Strict TDD  
**Delivery**: single-pr (size:exception accepted)  
**Artifact store**: hybrid (Engram + filesystem)

---

## Completed Tasks

All tasks from Phase 1 through Phase 5 were completed in a prior apply batch.  
Phase 6 (verification gap fixes) was completed in this batch.

### Phase 1 — Installments URL Fix
- [x] 1.1 Fix `installment.service.ts` baseUrl: removed spurious `/api/` prefix
- [x] 1.2 RED test: `getPlans()` URL has no `/api/api/`, matches `<apiUrl>/billing/installment-plans`
- [x] 1.3 RED test: `deferPurchase()` POSTs to `<apiUrl>/billing/installment-plans`; GREEN

### Phase 2 — Card Lifecycle Backend
- [x] 2.1–2.7 All handler unit tests, integration tests, service methods, DTOs, commands, controller actions — GREEN

### Phase 3 — Card Lifecycle Frontend
- [x] 3.1–3.5 Service and component tests, `card.service.ts` additions, `card-detail.component.ts` wiring — GREEN

### Phase 4 — Password Recovery Backend
- [x] 4.1–4.9 `PasswordResetToken` entity, migration, `PasswordResetService`, rate-limit policy, commands, controller — GREEN

### Phase 5 — Password Recovery Frontend
- [x] 5.1–5.6 `forgot-password` and `reset-password` component rewrites, `auth.service.ts` additions, routes — GREEN

### Phase 6 — Verification Gap Fixes (this batch)
- [x] 6.1 Gap 1: Base64Url encoding in `PasswordResetService`
- [x] 6.2 Gap 2: Timing protection via `DoTimingWork` in unknown-email branch
- [x] 6.3 Gap 3a: `forgot-password` DOM — `.alert-danger` block with `errorMessage`
- [x] 6.4 Gap 3b: `reset-password` DOM — `invalidLink` section with "Enlace Inválido" heading
- [x] 6.5 Gap 3c: `card-detail.component.ts` `newCard.id` → `newCard.newCardId`; `card.service.ts` typed return
- [x] 6.6 Gap 4: Named audit events (`issuer.card.blocked/unblocked/cancelled`); `BlockCardAsync` on `IssuerService`
- [x] 6.7 TDD evidence captured (see table below)

---

## Files Changed (Phase 6)

| File | Action | What Changed |
|------|--------|--------------|
| `backend/services/CardVault/src/CardVault.Api/Services/PasswordResetService.cs` | Modified | Unsealed; `Base64UrlEncode`; `DoTimingWork` method |
| `backend/services/CardVault/tests/CardVault.Tests/Services/PasswordResetServiceTests.cs` | Modified | RED tests for Base64Url and timing; `TrackingPasswordResetService` test-double |
| `backend/services/CardVault/src/CardVault.Api/Services/IssuerService.cs` | Modified | `BlockCardAsync` added; `UnblockCardAsync`/`CancelCardAsync` emit named audit events |
| `backend/services/CardVault/tests/CardVault.Tests/Services/IssuerServiceTests.cs` | Modified | RED tests for Unblock/Cancel named audit events |
| `backend/services/CardVault/src/CardVault.Api/Features/Issuer/Commands/IssuerCommands.cs` | Modified | `BlockCardCommandHandler` uses `BlockCardAsync` |
| `backend/services/CardVault/tests/CardVault.Tests/Handlers/CardLifecycleHandlerTests.cs` | Modified | RED test for Block named audit event |
| `frontend/src/app/features/auth/forgot-password.component.ts` | Modified | `.alert-danger` div with `*ngIf="errorMessage"` |
| `frontend/src/app/features/auth/forgot-password.component.spec.ts` | Modified | DOM RED test for `.alert-danger` presence |
| `frontend/src/app/features/auth/reset-password.component.ts` | Modified | `invalidLink` section; form guard updated to `!submitted && !invalidLink` |
| `frontend/src/app/features/auth/reset-password.component.spec.ts` | Modified | DOM RED test for "Enlace Inválido" text |
| `frontend/src/app/features/issuer/cards/card-detail.component.ts` | Modified | `newCard.id` → `newCard.newCardId` |
| `frontend/src/app/features/issuer/cards/card-detail.component.spec.ts` | Modified | Spy returns `{ newCardId: 'new-card-id' }`; navigation test updated |
| `frontend/src/app/features/issuer/cards/card.service.ts` | Modified | `replaceCard` return type `Observable<{newCardId:string}>` |
| `frontend/src/app/features/issuer/cards/card.service.spec.ts` | Modified | Flush updated to `{ newCardId: 'new-card-id' }` |

---

## TDD Cycle Evidence Table

| Gap | Task | RED (test written first) | GREEN (implementation) | REFACTOR |
|-----|------|--------------------------|------------------------|----------|
| Gap 1 | Base64Url token | `CreateToken_ShouldBeBase64UrlEncoded_NoUnsafeCharsOrPadding` — asserts no `+`, `/`, `=`; length 43 | `WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))` | No structural change needed |
| Gap 2 | Timing protection | `CreateToken_UnknownEmail_ShouldCallDoTimingWork` — `TrackingPasswordResetService` verifies `DoTimingWorkCalled == true` | Unsealed class; `protected virtual DoTimingWork`; called in unknown-email branch | No structural change needed |
| Gap 3a | forgot-password DOM | `should render error message element in the DOM when sendResetLink fails` — queries `.alert-danger` in fixture | Added `<div *ngIf="errorMessage" class="alert alert-danger mb-3">{{ errorMessage }}</div>` | No structural change needed |
| Gap 3b | reset-password DOM | `should render invalid-link section in the DOM when token is absent` — checks `textContent` contains `'Enlace Inválido'` | Added `<div *ngIf="invalidLink">...<h3>Enlace Inválido</h3>...</div>`; form guard updated | No structural change needed |
| Gap 3c | card-detail newCardId | Navigation test: `should navigate to the new card using newCardId from response` — asserts `/app/issuer/cards/new-card-id` | `newCard.id` → `newCard.newCardId` in component; `card.service.ts` typed return | No structural change needed |
| Gap 4 | Named audit events | 3 tests: `UnblockCard_ShouldEmitNamedUnblockedAuditEvent`, `CancelCard_ShouldEmitNamedCancelledAuditEvent`, `BlockCard_ShouldEmitNamedBlockedAuditEvent` | `BlockCardAsync` added; emit calls added in Unblock/Cancel; Handler updated | No structural change needed |

---

## Final Test Counts

| Suite | Baseline | Final | Status |
|-------|----------|-------|--------|
| CardVault.Tests | 280 | 284 | ✅ +4 new tests |
| IsoSwitch.Tests | 37 | 37 | ✅ unchanged |
| Frontend (Angular) | 90 | 93 | ✅ +3 new tests |
| **Total** | **407** | **414** | **✅ All passing** |

---

## Deviations from Design

- `PasswordResetService` unsealed (design assumed it could remain sealed) — necessary to allow `TrackingPasswordResetService` override in unit tests without a full interface extraction.
- Named audit events emitted **in addition to** the existing generic `issuer.card.status_changed` event (both present in audit log) — no spec conflict, additive behavior only.

---

## Status

**7/7 gap-fix tasks complete. All phases complete. Ready for sdd-verify re-run.**

### Workload / PR Boundary
- Mode: single-pr (size:exception)
- Estimated review budget: ~1050 lines total across all phases
- Exception recorded: maintainer accepted `size:exception` before apply started
