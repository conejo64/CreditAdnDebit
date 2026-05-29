# Proposal: v77 Collections Mutation Operations

## Intent

v76 delivered read-only visibility of delinquent accounts. Collections operators can now see accounts in arrears but have no way to record contact attempts or internal notes. This proposal adds the minimal write-side operations required for basic collections workflow: registering contact attempts and adding internal notes.

## Scope

### In Scope

- **Backend**:
  - Add `ContactAttemptEntity` and `DelinquencyNoteEntity` domain entities
  - Add EF Core migration for `contact_attempts` and `delinquency_notes` tables
  - Add MediatR commands: `RegisterContactAttemptCommand`, `AddDelinquencyNoteCommand`
  - Add MediatR queries: `GetContactAttemptsQuery`, `GetDelinquencyNotesQuery`
  - Extend `DelinquencyController` with POST/GET endpoints for contacts and notes
  - Add `collections:manage` permission and `CanManageCollections` authorization policy
  - Add validation: can't mutate `Resolved` records, notes max 1000 chars

- **Frontend**:
  - Add `delinquency-detail.component` with tabbed layout (Overview, Contact History, Notes)
  - Add forms for registering contact attempts and adding notes
  - Update `delinquency-list.component` with "View Details" button/link
  - Add route `/collections/delinquencies/:id`
  - Extend `delinquency.service` with POST/GET methods for contacts and notes

- **Authorization**:
  - Separate read (`CanViewCollections`) from write (`CanManageCollections`)
  - Write operations require `Admin`, `Operator` + `collections:manage` permission

### Out of Scope

- **Payment promises** (deferred to v78 — requires date-based state transitions and background worker)
- **Manual resolution** or escalation workflows (deferred to v78)
- **Kafka integration events** for collections actions (deferred to v78)
- **E2E tests** (deferred to collections quality slice after v78)
- **CRM or dialer integration** (future feature)
- **Editing or deleting contact attempts/notes** (immutable for audit integrity)

## Capabilities

### Modified Capabilities

- **`delinquency-management`**: Extend with write operations (contact attempts, internal notes)
- **`identity-and-access`**: Add `collections:manage` permission and `CanManageCollections` policy

## Approach

We'll follow the same read/write separation pattern established in v76: MediatR commands for mutations, immutable audit entities, explicit authorization policies, and Angular reactive forms. Contact attempts and notes are simpler than payment promises because they don't require date-based state transitions or background worker coordination. This keeps v77 focused, testable, and under the 400-line review budget.

The frontend will introduce a detail view (routed component) instead of inline editing in the list, maintaining clean separation between list/detail responsibilities and avoiding component complexity.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/services/CardVault/src/CardVault.Domain/Collections` | New | Add `ContactAttemptEntity`, `DelinquencyNoteEntity` |
| `backend/services/CardVault/src/CardVault.Infrastructure.Persistence/Billing` | Modified | Add DbSet + EF migration |
| `backend/services/CardVault/src/CardVault.Application/Features/Delinquency/Commands` | New | Add `RegisterContactAttemptCommand`, `AddDelinquencyNoteCommand` + handlers |
| `backend/services/CardVault/src/CardVault.Application/Features/Delinquency/Queries` | New | Add `GetContactAttemptsQuery`, `GetDelinquencyNotesQuery` + handlers |
| `backend/services/CardVault/src/CardVault.Api/Controllers/DelinquencyController.cs` | Modified | Add POST/GET endpoints for contacts and notes |
| `backend/services/CardVault/src/CardVault.Api/Security/PermissionCatalog.cs` | Modified | Add `CollectionsManage = "collections:manage"` |
| `backend/services/CardVault/src/CardVault.Api/Program.cs` | Modified | Add `CanManageCollections` policy |
| `frontend/src/app/features/collections/delinquency-detail.component.ts` | New | Detail view with tabbed layout |
| `frontend/src/app/features/collections/contact-attempt-form.component.ts` | New | Reactive form for contact attempt |
| `frontend/src/app/features/collections/note-form.component.ts` | New | Reactive form for internal note |
| `frontend/src/app/features/collections/delinquency.service.ts` | Modified | Add POST/GET methods |
| `frontend/src/app/app.routes.ts` | Modified | Add `/collections/delinquencies/:id` route |
| `frontend/src/app/features/collections/delinquency-list.component.ts` | Modified | Add "View Details" button |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Scope creep into payment promises | Medium | Explicitly defer payment promises to v78 in proposal and tasks. Stop after contact attempts + notes are complete. |
| Frontend detail component becomes too complex | Low | Use tabbed layout with separate tab components. Defer inline editing. |
| Auth policy drift (view vs manage confusion) | Low | Test both policies with integration tests. Add policy-boundary tests similar to v76. |
| Migration breaks existing v76 read flow | Very Low | Keep `GetDelinquentAccountsQuery` untouched. New tables have no FK constraints to `delinquency_records`. |
| Review budget exceeded (>400 lines) | Low | Phase 1 scope is minimal: 2 entities, 4 commands/queries, 1 detail component, 2 forms. Estimate ~350 changed lines. |

## Rollback Plan

- **Backend**:
  - Remove `ContactAttemptEntity` and `DelinquencyNoteEntity` from `CardVaultDbContext`
  - Rollback EF migration (`dotnet ef migrations remove`)
  - Remove commands, queries, and new endpoints from `DelinquencyController`
  - Remove `CanManageCollections` policy from `Program.cs`
- **Frontend**:
  - Remove `delinquency-detail` route from `app.routes.ts`
  - Remove detail component, form components, and extended service methods
  - Revert `delinquency-list.component` to v76 state (no "View Details" button)

## Dependencies

- v76-mora-temprana (archived) — read-only collections foundation
- Existing `DelinquencyRecordEntity` and `DelinquencyController`
- Existing `CanViewCollections` policy
- Frontend `collections` module and `delinquency.service`

## Success Criteria

- [ ] A user with `CanManageCollections` permission can register a contact attempt for an active delinquency record
- [ ] A user with `CanManageCollections` permission can add an internal note to an active delinquency record
- [ ] A user with only `CanViewCollections` permission receives 403 Forbidden when attempting write operations
- [ ] The system rejects contact attempts and notes for `Resolved` delinquency records with a validation error
- [ ] Contact attempts and notes appear in the detail view, sorted by timestamp descending
- [ ] The detail view displays delinquency overview (from v76), contact history tab, and notes tab
- [ ] All mutations are immutable (no edit/delete endpoints)
- [ ] Backend tests cover command validation, query correctness, and authorization policies
- [ ] Frontend tests cover form validation, service methods, and detail component tabs
