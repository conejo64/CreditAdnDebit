# Design: v76 Mora Temprana — Read-Only Delinquency Visibility

## Technical Approach

Surface `DelinquencyRecordEntity` data already produced by the background worker through a new MediatR query, a read-only controller, an authorization policy, and an Angular standalone component. No worker logic or schema changes are required.

## Architecture Decisions

| Decision | Options | Choice | Rationale |
|----------|---------|--------|-----------|
| Query location | `Features/Billing/Queries` vs `Features/Delinquency/Queries` | `Features/Delinquency/Queries` (new) | Command already lives under `Features/Delinquency/Commands`; keep read/write co-located by domain, not by module type |
| Pagination | Cursor vs offset | Offset (`page`/`pageSize`) | Existing billing queries use `take`-based patterns; offset is simpler for a first slice and consistent with `GetStatementsByAccountQuery` |
| Frontend feature location | `features/finance/` vs `features/collections/` | `features/collections/` | Distinct banking subdomain per exploration finding; avoids bloating the finance module |
| Authorized roles | `Admin` only vs `Admin + Operator` | `Admin, Operator` (+ granular `collections:view` claim) | Auditor excluded; granular claim allows per-user override without role promotion |
| Filter support | No filters vs `bucket` + `status` | `bucket` (optional) + `status` default `Active` | Operators need to triage by aging bucket; status filter enables showing historical resolved records |

## Data Flow

```
Angular DelinquencyListComponent
  └─ DelinquencyService.getDelinquencies(page, pageSize, bucket?)
       └─ GET /api/collections/delinquencies?page=1&pageSize=20&bucket=1
            └─ DelinquencyController [Authorize("CanViewCollections")]
                 └─ IMediator.Send(GetDelinquentAccountsQuery)
                      └─ GetDelinquentAccountsQueryHandler
                           └─ CardVaultDbContext.DelinquencyRecords
                                (filter Status=Active, optional Bucket)
                                .OrderByDescending(r => r.DaysInArrears)
                                .Skip/Take
                           └─ returns PagedResult<DelinquencyRecordDto>
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `CardVault.Api/Security/PermissionCatalog.cs` | Modify | Add `CollectionsView = "collections:view"` constant and entry in `All` + `Descriptions` |
| `CardVault.Api/Program.cs` | Modify | Add `CanViewCollections` policy: `Admin`, `Operator` + `CollectionsView` perm (Auditor excluded) |
| `CardVault.Api/Features/Delinquency/Queries/GetDelinquentAccountsQuery.cs` | Create | MediatR query, handler, DTO, and `PagedResult<T>` return |
| `CardVault.Api/Controllers/DelinquencyController.cs` | Create | `[Route("api/collections")]` controller with single `GET /delinquencies` endpoint |
| `frontend/src/app/features/collections/delinquency.service.ts` | Create | Angular service calling `GET /api/collections/delinquencies` |
| `frontend/src/app/features/collections/delinquency-list.component.ts` | Create | Standalone component: table + bucket filter + pagination |
| `frontend/src/app/app.routes.ts` | Modify | Add `collections/delinquency` route, `roleGuard` with `['Admin', 'Operator', 'Auditor']` |
| `frontend/src/app/layout/sidebar/sidebar.component.ts` | Modify | Add "Cobranzas" nav section with "Mora Temprana" entry |

## Interfaces / Contracts

```csharp
// Query
public record GetDelinquentAccountsQuery(
    int Page,
    int PageSize,
    DelinquencyBucket? Bucket,
    DelinquencyRecordStatus Status = DelinquencyRecordStatus.Active
) : IRequest<IResult>;

// Response DTO
public record DelinquencyRecordDto(
    Guid   Id,
    Guid   AccountId,
    Guid   StatementId,
    decimal OverdueAmount,
    int    DaysInArrears,
    int    Bucket,
    string BucketLabel,   // "1-30", "31-60", "61-90", ">90"
    string Status,        // "Active" | "Resolved"
    DateTimeOffset CreatedOn,
    DateTimeOffset UpdatedOn
);

// Pagination envelope (reusable, create once)
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
```

```typescript
// Angular service contract
export interface DelinquencyRecord {
  id: string;
  accountId: string;
  statementId: string;
  overdueAmount: number;
  daysInArrears: number;
  bucket: number;
  bucketLabel: '1-30' | '31-60' | '61-90' | '>90';
  status: 'Active' | 'Resolved';
  createdOn: string;
  updatedOn: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
```

**Endpoint**: `GET /api/collections/delinquencies?page=1&pageSize=20&bucket={1-4}&status=Active`  
**Response**: `200 PagedResult<DelinquencyRecordDto>` | `403 Forbidden` (no policy) | `401 Unauthorized`  
**No mutation endpoints** in this slice.

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit (backend) | `GetDelinquentAccountsQueryHandler`: filter by bucket/status, correct ordering, pagination math | xUnit + EF Core InMemory or SQLite |
| Unit (backend) | `CanViewCollections` policy: Admin/Operator pass, Auditor/anonymous fail, granular perm claim passes | ASP.NET policy unit tests with `AuthorizationService` |
| Integration (backend) | `GET /api/collections/delinquencies` returns 200 with data for authorized user, 403 for unauthorized | `WebApplicationFactory` |
| Unit (frontend) | `DelinquencyListComponent`: renders rows, bucket badge colors, pagination controls | Angular `TestBed` + `HttpClientTestingModule` |

## Migration / Rollout

No migration required. `DelinquencyRecords` table is already populated by the worker.  
Rollback: remove `DelinquencyController`, revert `Program.cs` policy entry, remove Angular route and component.

## Open Questions

- [ ] Should `PagedResult<T>` be a shared type in `BuildingBlocks` or remain in `CardVault.Application`? Existing billing queries use inline anonymous types — first slice can be local; promote to shared later.
- [ ] Does the `Auditor` role need delinquency visibility, or should it be restricted to `Admin` + `Operator` only? Decision mirrors `CanViewBilling` but should be confirmed with stakeholders.
