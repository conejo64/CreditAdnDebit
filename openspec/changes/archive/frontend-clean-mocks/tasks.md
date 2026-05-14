# Implementation Tasks: Frontend Clean Mocks

## Phase 1: Infrastructure
- [x] Create `frontend/src/app/core/notification.service.ts`
- [x] Add basic toast styles to `frontend/src/styles.css`
- [x] Update `frontend/src/app/app.component.ts` to include notifications container.

## Phase 2: Refactoring Components
- [x] **Customer List** (`customer-list.component.ts`):
    - Remove mocks from `loadCatalogs`.
    - Remove mocks from `loadCustomers`.
    - Inject `NotificationService` and show error on failure.
- [x] **Customer Detail** (`customer-detail.component.ts`):
    - Replace `alert()` on load error and on create account error.
    - Inject `NotificationService`.
- [x] **Simulator** (`simulator.component.ts`):
    - Remove mocks from `loadInitialData`.
    - Handle empty card/customer lists gracefully.
- [x] **Audit List** (`audit-list.component.ts`):
    - Remove mocks from `loadAuditLogs`.
- [x] **Card List** (`card-list.component.ts`):
    - Remove mocks from `loadCards`.
- [x] **Account List** (`account-list.component.ts`):
    - Inject `NotificationService` and show error on failure.
- [x] **Open Banking List** (`open-banking-list.component.ts`):
    - Replace all silent `catchError` with notifications.
    - Replace `alert()` in `saveClient` and `grantAccess`.
- [x] **Loyalty List** (`loyalty-list.component.ts`):
    - Replace all silent `catchError` with notifications.
    - Replace `alert()` in `saveProgram` and `saveCatalogItem`.
- [x] **Credit Limit List** (`credit-limit-list.component.ts`):
    - Replace all silent `catchError` with notifications.
    - Replace `alert()` in `applyProposal` and `runEvaluation`.
- [x] **Accounting List** (`accounting-list.component.ts`):
    - Replace all silent `catchError` with notifications.
    - Replace `alert()` in `saveAccount` and `saveMapping`.

## Phase 3: Verification
- [ ] Simulate backend failure and verify error messages appear.
- [ ] Verify that UI does not show "fake" customers or cards when the API fails.

## Out of Scope (deuda separada)
The following files still use `alert()` or silent errors but were not in scope for this change:
- `ledger-list.component.ts` (L241, L257, L262)
- `billing-statement.component.ts` (L213, L219)
- `account-limits-modal.component.ts` (L143, L149)
- `wallets-list.component.ts` (L283, L304)
- `settlement-list.component.ts`, `vault.component.ts`, `card-detail.component.ts`, `catalogs.component.ts`
