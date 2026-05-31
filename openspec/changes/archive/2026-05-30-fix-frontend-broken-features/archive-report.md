# Archive Report: fix-frontend-broken-features

**Change Name**: fix-frontend-broken-features  
**Archived Date**: 2026-05-30  
**Archive Location**: `openspec/changes/archive/2026-05-30-fix-frontend-broken-features/`  
**Artifact Store Mode**: hybrid  

---

## Verification Status

**Result**: ✅ **PASS WITH WARNINGS**

| Metric | Value |
|--------|-------|
| Tasks Total | 37 |
| Tasks Complete | 37 |
| Tasks Incomplete | 0 |
| Build Status | ✅ Passed |
| Tests Passed | 414 |
| Tests Failed | 0 |
| Tests Skipped | 0 |
| Coverage | Skipped (no tool detected) |
| TDD Compliance | 5/6 checks passed |

**Critical Issues**: None  
**Warnings**: HTTP integration coverage incomplete for specific edge-case paths (password reset 204, policy violations, card lifecycle 403/409). Unit/handler layer is well-covered.

---

## Specs Synced

### 1. Domain: http-contracts

**Status**: Updated  
**Changes**:
- **Added**: Requirement HC-1 (Installment Service Route MUST Resolve Without Duplicated Segment)
  - 2 scenarios: Plans list resolves without 404; existing `/api/api/` double-segment is gone
- **Added**: Requirement HC-2 (Five New Endpoint Contracts)
  - 5 endpoint specifications: `POST /api/issuer/cards/{id}/unblock`, `cancel`, `replace`, `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`
  - 7 scenarios: Unblock endpoint reachable; Cancel endpoint returns 204; Replace endpoint returns 201; Forgot-password enumeration-safe; Reset-password token validation

**File**: `openspec/specs/http-contracts/spec.md`

### 2. Domain: identity-and-access

**Status**: Updated (Extended)  
**Changes**:
- **Added**: Requirement IAM-PR-1 (Password Recovery Flow MUST Exist and Be Real)
- **Added**: Requirement IAM-PR-2 (Token Generation MUST Be Cryptographically Strong and Hash-Stored)
  - 2 scenarios: Token generation produces non-guessable value; Enumeration attack not possible via response differential
- **Added**: Requirement IAM-PR-3 (Token Validation MUST Enforce Expiry, Single-Use, and Password Policy)
  - 4 scenarios: Valid token resets password; Expired token rejected; Reused token rejected; Password policy violation rejected
- **Added**: Requirement IAM-PR-4 (Frontend Components MUST Be Real HTTP Consumers)
  - 4 scenarios: Success card shown only after real 2xx; Error card on non-2xx; Reset-password page validates token; Reset-password end-to-end success
- **Updated**: Endpoints Protected table with new password recovery endpoints

**File**: `openspec/specs/identity-and-access/spec.md`

### 3. Domain: issuer-ledger-billing

**Status**: Updated (Expanded)  
**Changes**:
- **Replaced**: Generic "Card lifecycle actions are recorded" scenario with explicit ILB-CL requirements
- **Added**: Requirement ILB-CL-1 (Card Lifecycle MUST Be Complete — Four Operations)
  - 6 scenarios: Block (regression), Unblock, Cancel, Replace, Unauthorized rejection, Replace on cancelled card returns 409
- **Added**: Requirement ILB-CL-2 (Replace MUST Maintain Audit Linkage Between Cards)
  - 2 scenarios: Bidirectional audit trail after replace; New card inherits account, not PAN
- **Added**: Requirement ILB-CL-3 (Domain Audit Events MUST Be Emitted for Every Lifecycle Transition)
  - 2 scenarios: Event emitted after successful lifecycle action; No event on failed operation

**File**: `openspec/specs/issuer-ledger-billing/spec.md`

---

## Implementation Summary

### Capabilities Fixed

1. **Installments** (HC-1)
   - Fixed URL construction: `${environment.apiUrl}/billing` instead of `${environment.apiUrl}/api/billing`
   - Eliminated `/api/api/` double-segment issue
   - Added smoke tests for route resolution

2. **Card Lifecycle** (ILB-CL-1, ILB-CL-2, ILB-CL-3)
   - Implemented 3 new backend endpoints: Unblock, Cancel, Replace
   - Each emits domain audit events (`CardUnblockedEvent`, `CardCancelledEvent`, `CardReplacedEvent`)
   - Replace maintains bidirectional audit linkage and issues new card under same account
   - Frontend wired with service methods and UI buttons for all 4 operations
   - 403 rejection for unauthorized callers, 409 for conflict cases

3. **Password Recovery** (IAM-PR-1, IAM-PR-2, IAM-PR-3, IAM-PR-4)
   - Implemented `PasswordResetService` with cryptographically strong token generation (256-bit CSPRNG)
   - Token hashing: SHA-256 hash persisted, never plaintext
   - Two new endpoints: `forgot-password` (202, enumeration-safe), `reset-password` (204 on valid token)
   - Rate limiting applied to both endpoints
   - Frontend: Real HTTP integration (no emulation), success/error states tied to responses
   - New `reset-password.component.ts` with token validation from query parameter

### Test Coverage

- **Backend**: 321 tests passed (CardVault.Tests + IsoSwitch.Tests)
- **Frontend**: 93 tests passed (Angular Chrome Headless)
- **Total**: 414 tests green, 0 failed
- **TDD Evidence**: All tasks include test coverage; RED → GREEN cycle documented

---

## Archive Contents

- ✅ `proposal.md` — Full change rationale and scope
- ✅ `specs/` — Delta specs for 3 domains (http-contracts, identity-and-access, issuer-ledger-billing)
- ✅ `design.md` — Technical design and architecture decisions
- ✅ `tasks.md` — 37 implementation tasks, all marked complete
- ✅ `verification.md` — Full verification report (PASS WITH WARNINGS)
- ✅ `apply-progress.md` — TDD cycle evidence and implementation progress

---

## Source of Truth Updated

The following main specs now reflect the implemented behavior:

| Spec | Changes | Link |
|------|---------|------|
| http-contracts | HC-1 installment route + HC-2 five new endpoints | `openspec/specs/http-contracts/spec.md` |
| identity-and-access | IAM-PR-1 through IAM-PR-4 password recovery | `openspec/specs/identity-and-access/spec.md` |
| issuer-ledger-billing | ILB-CL-1 through ILB-CL-3 complete lifecycle | `openspec/specs/issuer-ledger-billing/spec.md` |

---

## SDD Cycle Complete

✅ **Proposed** — Change proposal and rationale captured  
✅ **Specified** — Delta specs defined and aligned with stakeholders  
✅ **Designed** — Technical approach documented (3 slices: installments, card lifecycle, password recovery)  
✅ **Implemented** — All 37 tasks complete; code committed and tested  
✅ **Verified** — PASS WITH WARNINGS; critical gaps resolved  
✅ **Archived** — Specs synced to main; change moved to archive; this report persisted  

**Status**: Ready for the next change. All three broken features are now real, tested, and documented in the spec baseline.

---

## Warnings and Notes

- **HTTP Edge-Case Coverage**: Integration tests for password reset 204, policy violations, and card lifecycle 403/409 remain at unit/handler layer only. Suggestion: Implement additional WebApplicationFactory tests for these specific failure responses.
- **Notification Channel Dependency**: Password recovery slice assumes `INotificationProvider` is available. If `real-notification-channels` is not yet merged, the handler emits to the abstraction; acceptance testing of email dispatch waits for the channel to land.
- **Timing Protection**: Phase 6 gap fixes added protection against enumeration attacks via response timing. Timing-safe response generation is in place.

---

**Archive Report Created**: 2026-05-30  
**Report Location**: `openspec/changes/archive/2026-05-30-fix-frontend-broken-features/archive-report.md`
