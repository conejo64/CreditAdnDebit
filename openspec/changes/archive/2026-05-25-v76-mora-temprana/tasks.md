# Tasks: v76 Mora Temprana — Read-Only Delinquency Visibility

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 350–500 |
| 400-line budget risk | Medium-High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (backend) → PR 2 (frontend) |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Backend: policy, query handler, controller | PR 1 → main | Self-contained; includes unit + integration tests |
| 2 | Frontend: service, component, routing, sidebar | PR 2 → main | Depends on PR 1 endpoint being available |

---

## Phase 1: Backend Foundation

- [x] 1.1 Add `CollectionsView = "collections:view"` to `CardVault.Api/Security/PermissionCatalog.cs` (`All` list + `Descriptions` dict).
- [x] 1.2 Register `CanViewCollections` policy in `CardVault.Api/Program.cs` with roles `Admin`, `Operator` and permission `CollectionsView`. (Auditor excluded per resolved decision.)
- [x] 1.3 Create `CardVault.Api/Features/Delinquency/Queries/GetDelinquentAccountsQuery.cs` — define `GetDelinquentAccountsQuery` record, `DelinquencyRecordDto` record, and `PagedResult<T>` local type.

## Phase 2: Core Backend Implementation

- [x] 2.1 Implement `GetDelinquentAccountsQueryHandler` in the same file: query `CardVaultDbContext.DelinquencyRecords`, filter by `Status` (default `Active`) and optional `Bucket`, order by `DaysInArrears` descending, apply `Skip`/`Take`, return `PagedResult<DelinquencyRecordDto>`.
- [x] 2.2 Create `CardVault.Api/Controllers/DelinquencyController.cs` with `[Route("api/collections")]`, `[Authorize("CanViewCollections")]`, and single `GET /delinquencies` action; bind `page`, `pageSize`, `bucket`, `status` query params; delegate to `IMediator`.

## Phase 3: Frontend Implementation

- [x] 3.1 Create `frontend/src/app/features/collections/delinquency.service.ts` — `DelinquencyRecord` and `PagedResult<T>` interfaces; `getDelinquencies(page, pageSize, bucket?)` calling `GET /api/collections/delinquencies`.
- [x] 3.2 Create `frontend/src/app/features/collections/delinquency-list.component.ts` — standalone component: data table with bucket badge colors, bucket filter dropdown, pagination controls; inject `DelinquencyService`.
- [x] 3.3 Add `collections/delinquency` route in `frontend/src/app/app.routes.ts` with `roleGuard(['Admin', 'Operator'])` (Auditor excluded per resolved policy decision) and lazy-load the new component.
- [x] 3.4 Add "Cobranzas" nav section with "Mora Temprana" entry in `frontend/src/app/layout/sidebar/sidebar.component.ts` (roles: Admin, Operator only).

## Phase 4: Testing

- [x] 4.1 Write xUnit unit tests for `GetDelinquentAccountsQueryHandler`: filter by bucket, filter by status, default Active status, ordering by `DaysInArrears`, pagination math (skip/take). Use EF Core InMemory or SQLite.
- [x] 4.2 Write policy unit tests for `CanViewCollections`: Admin/Operator pass; Auditor (without explicit perm) and anonymous fail. RoleOrPerm predicate verified in isolation.
- [x] 4.3 Write `WebApplicationFactory` integration test: `GET /api/collections/delinquencies` returns `200` with `PagedResult` for authorized user; `403` for unauthorized; `401` for anonymous. Runtime read-only proof: `POST /api/collections/delinquencies` by authorized caller returns `405 Method Not Allowed`.
- [x] 4.4 Write Angular `TestBed` unit tests for `DelinquencyListComponent`: renders table rows from mock data, bucket badge displayed, pagination controls trigger service call, 403 error surfaced truthfully (not as empty-state), 500 error surfaced truthfully. Use `HttpClientTestingModule`.
- [x] 4.4b Write route auth alignment spec `collections-routes-auth.spec.ts`: Auditor excluded from delinquency route data, roleGuard present.

## Phase 5: Cleanup

- [x] 5.1 Confirm with stakeholders whether `Auditor` should be included in `CanViewCollections` — decision resolved: Auditor is EXCLUDED. Route, sidebar, and policy all aligned.
- [x] 5.2 Decide `PagedResult<T>` location — keep local to `CardVault.Application` or promote to `BuildingBlocks`. Document decision as a comment in the class file.
- [x] 5.3 Update `openspec/changes/v76-mora-temprana/tasks.md` checkboxes to reflect completed items before archiving.

## Phase 6: End-to-End Auth Alignment (Final Slice)

- [x] 6.1 Export `isRouteAuthorized(user, allowedRoles, requiredPermissions)` pure function from `role.guard.ts` — mirrors `CanViewCollections` backend contract (role OR granular permission).
- [x] 6.2 Update `AuthService.ensureAuthorized` to accept optional `requiredPermissions` param and apply OR logic (role match OR permission match).
- [x] 6.3 Update `roleGuard` to read `route.data['permissions']` and pass to `ensureAuthorized`.
- [x] 6.4 Add `permissions: ['collections:view']` to the `collections/delinquency` route data in `app.routes.ts`.
- [x] 6.5 Extend `MenuItem` interface in sidebar to support optional `permissions?: string[]`; update `canAccess()` to grant access if user has a matching permission when no role matches.
- [x] 6.6 Add `permissions: ['collections:view']` to the Cobranzas sidebar item for Mora Temprana.
- [x] 6.7 Write/extend unit tests: `role.guard.spec.ts` (8 specs covering role, permission, combined, null), `sidebar.component.spec.ts` (+4 specs for permissions), `collections-routes-auth.spec.ts` (+1 spec for data.permissions). All 13 new tests GREEN.

