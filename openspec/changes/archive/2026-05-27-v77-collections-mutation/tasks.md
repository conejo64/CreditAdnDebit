# Tasks: v77 Collections Mutation Operations

## Overview

This change adds write operations for collections management: contact attempts and internal notes. Tasks are organized following work-unit commit principles (tests + production code together) and grouped to stay under the 400-line review budget.

**Total estimated changed lines**: ~380  
**Chained PRs recommended**: No (under budget)  
**Estimated task count**: 10

---

## Backend Tasks

### Task 1: Add domain entities and EF migration

**Estimate**: ~60 lines

- [x] Create `ContactAttemptEntity` in `CardVault.Infrastructure.Persistence/Collections/`
  - Fields: Id, DelinquencyRecordId, Channel (enum), Outcome (enum), Notes (nullable), AttemptedBy, AttemptedOn
- [x] Create `DelinquencyNoteEntity` in `CardVault.Infrastructure.Persistence/Collections/`
  - Fields: Id, DelinquencyRecordId, Content, CreatedBy, CreatedOn
- [x] Create `ContactChannel` enum: Phone, Email, SMS, InPerson
- [x] Create `ContactOutcome` enum: Contacted, NoAnswer, InvalidContact, CustomerRefused
- [x] Add `DbSet<ContactAttemptEntity>` and `DbSet<DelinquencyNoteEntity>` to `CardVaultDbContext`
- [x] EF model configuration added (ToTable, HasKey, HasIndex, MaxLength constraints)
- [x] Migration: handled by EF InMemory in tests; production migration to be run on deployment

**Acceptance**: Entities compile, DbSets created, 17 structural tests pass.

---

### Task 2: Add RegisterContactAttemptCommand + handler + validation

**Estimate**: ~70 lines

- [x] Create `RegisterContactAttemptCommand` in `CardVault.Api/Features/Delinquency/Commands/`
  - Fields: DelinquencyRecordId, Channel, Outcome, Notes (optional)
- [x] Create `RegisterContactAttemptCommandHandler`
  - Validate: delinquency record exists and is `Active` (not `Resolved`)
  - Persist `ContactAttemptEntity` with user identity
  - Return new attempt ID (Guid)
- [x] Tests:
  - `Handle_PersistsContactAttempt_WhenRecordIsActive`
  - `Handle_AcceptsNullNotes`
  - `Handle_Throws_WhenDelinquencyRecordNotFound`
  - `Handle_Throws_WhenDelinquencyRecordIsResolved`

**Acceptance**: All tests pass, command persists contact attempts only for active records.

---

### Task 3: Add AddDelinquencyNoteCommand + handler + validation

**Estimate**: ~70 lines

- [x] Create `AddDelinquencyNoteCommand` in `CardVault.Api/Features/Delinquency/Commands/`
  - Fields: DelinquencyRecordId, Content, CreatedBy
- [x] Create `AddDelinquencyNoteCommandHandler`
  - Validate: delinquency record exists and is `Active`
  - Persist `DelinquencyNoteEntity`
  - Return new note ID (Guid)
- [x] Tests:
  - `Handle_PersistsNote_WhenRecordIsActive`
  - `Handle_Throws_WhenDelinquencyRecordNotFound`
  - `Handle_Throws_WhenDelinquencyRecordIsResolved`

**Acceptance**: All tests pass, command persists notes only for active records.

---

### Task 4: Add GetContactAttemptsQuery + handler

**Estimate**: ~40 lines

- [x] Create `GetContactAttemptsQuery` in `CardVault.Api/Features/Delinquency/Queries/`
  - Fields: DelinquencyRecordId
- [x] Create `GetContactAttemptsQueryHandler`
  - Query `ContactAttemptEntity` filtered by `DelinquencyRecordId`
  - Order by `AttemptedOn` descending
  - Return list of entities
- [x] Tests:
  - `Handle_ReturnsAttempts_SortedByTimestampDescending`
  - `Handle_ReturnsEmpty_WhenNoAttemptsExist`
  - `Handle_OnlyReturnsAttemptsForSpecifiedRecord`

**Acceptance**: Query returns contact attempts sorted correctly.

---

### Task 5: Add GetDelinquencyNotesQuery + handler

**Estimate**: ~40 lines

- [x] Create `GetDelinquencyNotesQuery` in `CardVault.Api/Features/Delinquency/Queries/`
  - Fields: DelinquencyRecordId
- [x] Create `GetDelinquencyNotesQueryHandler`
  - Query `DelinquencyNoteEntity` filtered by `DelinquencyRecordId`
  - Order by `CreatedOn` descending
  - Return list of entities
- [x] Tests:
  - `Handle_ReturnsNotes_SortedByCreatedOnDescending`
  - `Handle_ReturnsEmpty_WhenNoNotesExist`
  - `Handle_OnlyReturnsNotesForSpecifiedRecord`

**Acceptance**: Query returns notes sorted correctly.

---

### Task 6: Extend DelinquencyController with mutation endpoints

**Estimate**: ~50 lines

- [x] Add POST `/api/collections/delinquencies/{id}/contact-attempts` endpoint
  - Authorize: `CanManageCollections`
  - Send `RegisterContactAttemptCommand` via MediatR; returns 201
- [x] Add GET `/api/collections/delinquencies/{id}/contact-attempts` endpoint
  - Authorize: `CanViewCollections` (controller-level)
  - Send `GetContactAttemptsQuery` via MediatR
- [x] Add POST `/api/collections/delinquencies/{id}/notes` endpoint
  - Authorize: `CanManageCollections`
  - Send `AddDelinquencyNoteCommand` via MediatR; returns 201
- [x] Add GET `/api/collections/delinquencies/{id}/notes` endpoint
  - Authorize: `CanViewCollections` (controller-level)
  - Send `GetDelinquencyNotesQuery` via MediatR

**Acceptance**: 10 structural tests pass. Endpoints compile and route correctly.

---

### Task 7: Add collections:manage permission and CanManageCollections policy

**Estimate**: ~30 lines

- [x] Add `CollectionsManage = "collections:manage"` to `PermissionCatalog.cs`
- [x] Add `CollectionsManage` to `PermissionCatalog.All` list and `Descriptions` dictionary
- [x] Add `CanManageCollections` policy to `Program.cs`:
  - Authorize: `Admin`, `Operator`, OR `collections:manage` claim
  - Exclude: `Auditor`
- [x] Auth boundary tests:
  - `CanManageCollections_AdminAllowed`
  - `CanManageCollections_OperatorAllowed`
  - `CanManageCollections_AuditorDenied`
  - `CanManageCollections_AnonymousDenied`
  - `CanManageCollections_GranularPermAllowed`

**Acceptance**: All 8 auth/catalog tests pass.

---

## Frontend Tasks

### Task 8: Add delinquency-detail component with routing

**Estimate**: ~80 lines

- [x] Create `delinquency-detail.component.ts` in `frontend/src/app/features/collections/`
  - Standalone component, inject `ActivatedRoute`, `DelinquencyService`
  - Load delinquency record by ID from route params (via `getDelinquencies` list endpoint)
  - Display overview section (account, status, bucket, days, amount)
  - Two tabs: "Contact History" and "Notes"
- [x] Add route in `app.routes.ts`:
  - Path: `collections/delinquencies/:id` (under `/app`)
  - Guard: `roleGuard` with `['Admin', 'Operator']` + `permissions: ['collections:view']`
- [x] Update `delinquency-list.component.ts`:
  - Added `RouterModule` import
  - Added "Ver detalle" link per row with `data-testid="view-details-link"`
- [x] Component tests: 6 tests covering load, overview display, tabs, contact/note data

**Acceptance**: Route wired, detail component renders, 6 tests pass.

---

### Task 9: Add contact attempt and note forms

**Estimate**: ~90 lines

- [x] Create `contact-attempt-form.component.ts`
  - Reactive form: channel (dropdown), outcome (dropdown), notes (textarea, max 1000 chars)
  - On submit: call `delinquencyService.registerContactAttempt()`
  - Handle success (emit `submitted` event) and error (display validation)
- [x] Create `note-form.component.ts`
  - Reactive form: content (textarea, required, max 1000 chars)
  - On submit: call `delinquencyService.addNote()`
  - Handle success/error similarly
- [x] Forms integrated into `delinquency-detail.component`
- [x] Form validation tests (12 total):
  - `ContactAttemptForm_RequiredFieldsValidation` (channel + outcome)
  - `ContactAttemptForm_NotesMaxLength`
  - `ContactAttemptForm_ValidSubmit`
  - `ContactAttemptForm_EmittedEvent`
  - `NoteForm_RequiredContent`
  - `NoteForm_ContentMaxLength`
  - `NoteForm_ValidSubmit`
  - `NoteForm_EmittedEvent`

**Acceptance**: Forms validate correctly, submit to service methods, emit events on success.

---

### Task 10: Extend DelinquencyService with mutation methods

**Estimate**: ~50 lines

- [x] Add interfaces: `ContactAttempt`, `DelinquencyNote`
- [x] Add `registerContactAttempt(delinquencyRecordId, channel, outcome, notes?)` method
  - POST to `/api/collections/delinquencies/${id}/contact-attempts`
- [x] Add `getContactAttempts(delinquencyRecordId)` method
  - GET from `/api/collections/delinquencies/${id}/contact-attempts`
- [x] Add `addNote(delinquencyRecordId, content)` method
  - POST to `/api/collections/delinquencies/${id}/notes`
- [x] Add `getNotes(delinquencyRecordId)` method
  - GET from `/api/collections/delinquencies/${id}/notes`
- [x] Service tests: 5 new tests for mutation methods

**Acceptance**: Service methods call correct endpoints with proper payloads. All 5 tests pass.

---

## Review Workload Forecast

| Metric | Value |
|--------|-------|
| **Total estimated changed lines** | ~380 |
| **Chained PRs recommended** | No |
| **400-line budget risk** | Low |
| **Decision needed before apply** | No |

**Rationale**: Phase 1 scope (contact attempts + notes) is minimal CRUD. Backend: 2 entities, 2 commands, 2 queries, 4 endpoints, 1 policy = ~210 lines. Frontend: 1 detail component, 2 forms, 1 service extension = ~170 lines. Total under 400-line budget.

---

## Dependencies

- Task 2-3 depend on Task 1 (entities + migration)
- Task 6 depends on Task 2-5 (commands + queries)
- Task 9 depends on Task 8 (detail component must exist before forms integrate)
- Task 9-10 can run in parallel after Task 8

---

## Completion Status

**All 10 tasks complete.**

- Backend safety net: 195/195 tests passing (was 147; +48 new tests)
- Frontend: 41/41 tests passing (was 9; +32 new tests)

## Next Steps

1. Run EF migration on dev database: `dotnet ef migrations add AddCollectionsMutationTables --project backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
2. Launch `sdd-verify` to validate against spec scenarios
3. Archive change to `openspec/changes/archive/`
