# Archive Report: v76-mora-temprana

**Date Archived**: 2026-05-25  
**Change Name**: v76-mora-temprana (Mora Temprana — Early Delinquency Visibility)  
**Archive Path**: `openspec/changes/archive/2026-05-25-v76-mora-temprana/`  
**Artifact Store Mode**: hybrid (OpenSpec files + Engram)

## Executive Summary

The v76-mora-temprana change has been successfully archived after full implementation and verification. This read-only backend and frontend slice introduces collections visibility through a new `DelinquencyController` API and corresponding Angular UI components. All 24 implementation tasks are complete, and verification passed with non-blocking warnings around E2E test coverage and strict TDD traceability.

## Verification Status

**Final Verdict**: PASS WITH WARNINGS ✅  
**Verification Date**: (from verify-report.md)

### Key Outcomes
- ✅ **Tasks Complete**: 24/24 (100%)
- ✅ **Build Status**: Passed
- ✅ **Tests**: 57 passed / 0 failed / 0 skipped
- ✅ **Spec Compliance**: 5/5 requirements compliant
- ⚠️ **Warnings**: Missing E2E tests, partial strict TDD traceability, incomplete runtime proof of guard/AuthService path

### Test Coverage by Layer
| Layer | Count | Files |
|-------|-------|-------|
| Unit | 34 | 7 |
| Integration | 23 | 4 |
| E2E | 0 | — |
| **Total** | **57** | **11** |

## Specs Synced

### 1. Delinquency Management (`delinquency-management/spec.md`)
**Action**: Created (new domain spec)  
**Requirements Added**:
- Requirement: Read-Only Collections Visibility

**Summary**: Establishes visibility and tracking of accounts in arrears with aging buckets, without mutation capabilities.

### 2. Identity And Access (`identity-and-access/spec.md`)
**Action**: Updated (appended new requirement)  
**Requirements Added**:
- Requirement: Collections Visibility Policy

**Summary**: Extends existing auth model with granular `CanViewCollections` policy for collections data access.

**No Requirements Modified or Removed**.

## Archive Contents Manifest

✅ `proposal.md` — Scope, risks, rollback plan, dependencies  
✅ `exploration.md` — Technical exploration notes  
✅ `design.md` — Architecture and design decisions  
✅ `tasks.md` — 24 implementation tasks (all complete)  
✅ `verify-report.md` — Full verification report with test evidence  
✅ `specs/delinquency-management/spec.md` — Read-only collections visibility spec  
✅ `specs/identity-and-access/spec.md` — Collections authorization policy spec  

## Implementation Summary

### Backend Changes
- **New**: `DelinquencyController` — read-only HTTP endpoint for querying delinquent accounts
- **New**: `GetDelinquentAccountsQuery` (MediatR) — paginated query with filtering by aging bucket
- **New**: `CanViewCollections` authorization policy — restricts access to users/roles with collections view permission
- **Modified**: `PermissionCatalog` — added `collections:view` permission definition
- **Test Coverage**: 28/28 backend tests passed (100%)

### Frontend Changes
- **New**: `collections` feature module — lazy-loaded Angular module
- **New**: `delinquency-list.component` — displays paginated delinquent accounts with aging buckets
- **New**: `delinquency.service` — API client for backend collections endpoint
- **New**: Route `GET /collections/delinquencies` — lazy-loaded view under `/collections`
- **Modified**: `sidebar.component` — added collections nav entry (role/permission filtered)
- **Modified**: `auth.service` — extended with granular `ensureAuthorized(...)` for `collections:view` claim
- **Modified**: `role.guard` — supports both role-based and claim-based authorization
- **Modified**: `app.routes` — added lazy route for collections module
- **Test Coverage**: 29/29 frontend tests passed (100%)

### Key Design Decisions
1. **Read-Only First**: Collections are read-only in this slice; mutation operations deferred to v77+
2. **Granular Authorization**: Support both role-based (`CanViewCollections` policy) and fine-grained permission (`collections:view` claim)
3. **Local `PagedResult<T>`**: Backend keeps pagination contract local; frontend documents the design decision
4. **Lazy Route**: Collections UI is lazy-loaded to optimize initial bundle size
5. **Existing Worker**: Leverages existing `DelinquencyRecordEntity` data populated by background worker

## Warnings and Deferred Items

### Non-Blocking Warnings
1. **Missing E2E Tests**: The collections module has no end-to-end tests. These are deferred to a future collections slice.
2. **Partial Strict TDD Traceability**: Only PR4 has row-level RED/GREEN/TRIANGULATE evidence; PR1-PR3 are summarized narratively.
3. **Incomplete Runtime Guard Proof**: The final frontend guard/AuthService path is verified mostly by code inspection and pure-function tests, not by direct runtime execution of the guard callback or `ensureAuthorized(...)` path.
4. **Stale Design Doc**: `design.md` predates final code changes and does not fully document the `permissions` route data and sidebar contract.
5. **Existing Build Warnings**: OpenTelemetry 1.9.0 moderate vulnerability (NU1902) and Angular Node/bundle warnings remain in the workspace; they did not block this slice.

### Deferred to v77+
- Mutation actions (registering contact, tracking outcomes)
- E2E integration tests
- Stronger runtime proof of guard/AuthService wiring

## Compliance Matrix

| Requirement | Spec Section | Scenarios | Test Evidence | Status |
|-------------|--------------|-----------|---|--------|
| Read-Only Collections Visibility | `delinquency-management` | 3 | Backend integration + frontend component | ✅ Compliant |
| Collections Visibility Policy | `identity-and-access` | 2 | Backend auth endpoint tests | ✅ Compliant |

All 5 spec scenarios verified via direct test execution.

## Source of Truth Updated

The following specs are now authoritative:
- **`openspec/specs/delinquency-management/spec.md`** (NEW) — Read-only collections and delinquency visibility
- **`openspec/specs/identity-and-access/spec.md`** (UPDATED) — Extended with Collections Visibility Policy requirement

These main specs reflect the final behavior and serve as the basis for future changes in the collections domain.

## SDD Cycle Complete ✅

**Phases Executed**:
1. ✅ **sdd-init** — Initialized SDD context and project structure
2. ✅ **sdd-explore** — Explored technical feasibility and design space
3. ✅ **sdd-propose** — Created change proposal with scope and approach
4. ✅ **sdd-spec** — Wrote delta specs with requirements and scenarios
5. ✅ **sdd-design** — Designed architecture and implementation approach
6. ✅ **sdd-tasks** — Broke change into 24 implementation tasks
7. ✅ **sdd-apply** — Executed all tasks; backend and frontend code complete
8. ✅ **sdd-verify** — Ran build, tests, and spec compliance checks (PASS WITH WARNINGS)
9. ✅ **sdd-archive** — Merged specs, archived change folder, persisted archive report

The change is ready for the next collections improvement in v77+.

## Next Steps

1. **Integration Testing**: Future slice should add E2E tests covering the full guard/AuthService flow
2. **v77 Mutation Slice**: Add contact registration, outcome tracking, and other write operations
3. **Documentation Backfill**: Update `design.md` with final route/permission contracts (optional cleanup)
4. **Workspace Warnings**: Address `NU1902` and Angular build warnings in a separate maintenance effort

## Archive Traceability (Engram Observations)

The following observations are linked to this change in Engram for full audit trail:

- `sdd/v76-mora-temprana/proposal` — Initial proposal
- `sdd/v76-mora-temprana/exploration` — Technical exploration
- `sdd/v76-mora-temprana/spec` — Delta specs
- `sdd/v76-mora-temprana/design` — Architecture design
- `sdd/v76-mora-temprana/tasks` — Implementation task checklist
- `sdd/v76-mora-temprana/verify-report` — Verification results
- `sdd/v76-mora-temprana/archive-report` — This archive report

The archived change folder in `openspec/changes/archive/` and this report serve as the permanent audit trail.

---

**Archive completed by**: sdd-archive executor  
**Timestamp**: 2026-05-25  
**Status**: ✅ Complete and ready for next iteration
