# Exploration: v77 Collections Mutation Operations

## Context

v76-mora-temprana delivered **read-only** collections visibility: operators can query delinquent accounts, see aging buckets, and filter by status. The backend exposes `GET /api/collections/delinquencies` with `CanViewCollections` policy, and the frontend displays paginated results in a dedicated Angular module.

The v76 archive report explicitly deferred:
- Mutation actions: registering contact attempts, tracking outcomes
- Write-side authorization (who can mutate collections records)
- Payment promises and status transitions

This exploration scopes v77 as the **minimal useful write-side slice** that complements v76.

---

## Business Context

### Collections Workflow (Industry Standard)

1. **Detection**: System identifies delinquent accounts (already done by background worker)
2. **Visibility**: Operator views accounts by aging bucket (✅ v76)
3. **Contact**: Operator attempts contact through multiple channels (📍 v77 target)
4. **Negotiation**: Operator records payment promises, notes, next actions (📍 v77 target)
5. **Tracking**: System tracks outcomes, escalation triggers, resolution (📍 v77 target)
6. **Resolution**: System or operator marks resolved when payment received (🔜 future)

v77 should cover **#3-4**: contact attempts and negotiation tracking.

---

## Proposed Mutation Operations

### 1. Register Contact Attempt

**Use case**: Operator calls/emails customer, records the attempt.

**Fields**:
- `DelinquencyRecordId` (FK)
- `Channel` (enum: Phone, Email, SMS, InPerson)
- `Outcome` (enum: Contacted, NoAnswer, InvalidContact, CustomerRefused)
- `Notes` (optional: free text)
- `AttemptedBy` (user who made the attempt)
- `AttemptedOn` (timestamp)

**Business rules**:
- Can't register contact for `Resolved` records
- Each contact attempt is immutable (audit trail)

### 2. Record Payment Promise

**Use case**: Customer promises to pay by a certain date.

**Fields**:
- `DelinquencyRecordId` (FK)
- `PromisedAmount` (decimal)
- `PromisedDate` (date)
- `Notes` (optional)
- `RecordedBy` (user)
- `RecordedOn` (timestamp)
- `Status` (enum: Pending, Kept, Broken) — system updates when due date passes

**Business rules**:
- Can't promise for `Resolved` records
- Promised amount can't exceed `OverdueAmount`
- Payment worker should check promises and auto-update status

### 3. Add Internal Note

**Use case**: Operator adds context (e.g., "customer lost job", "payment plan needed").

**Fields**:
- `DelinquencyRecordId` (FK)
- `Content` (text)
- `CreatedBy` (user)
- `CreatedOn` (timestamp)

**Business rules**:
- Notes are immutable (audit trail)
- Soft limit: 1000 chars

### 4. Manual Status Transition (Optional — Nice to Have)

**Use case**: Operator manually resolves or escalates.

**Operations**:
- `MarkAsResolved(reason)` — if payment worker missed it or external payment confirmed
- `Escalate(level)` — trigger legal collection, credit bureau report

**Business rules**:
- Only `Admin` or `collections:manage` perm can manually resolve
- Escalation requires specific role/permission

---

## Data Model Additions

### New Entities

```csharp
public sealed class ContactAttemptEntity
{
    public Guid Id { get; set; }
    public Guid DelinquencyRecordId { get; set; }
    public ContactChannel Channel { get; set; }
    public ContactOutcome Outcome { get; set; }
    public string? Notes { get; set; }
    public string AttemptedBy { get; set; }  // User email or ID
    public DateTimeOffset AttemptedOn { get; set; }
}

public sealed class PaymentPromiseEntity
{
    public Guid Id { get; set; }
    public Guid DelinquencyRecordId { get; set; }
    public decimal PromisedAmount { get; set; }
    public DateOnly PromisedDate { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; }
    public DateTimeOffset RecordedOn { get; set; }
    public PromiseStatus Status { get; set; }  // Pending, Kept, Broken
}

public sealed class DelinquencyNoteEntity
{
    public Guid Id { get; set; }
    public Guid DelinquencyRecordId { get; set; }
    public string Content { get; set; }
    public string CreatedBy { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}
```

### Enums

```csharp
public enum ContactChannel { Phone, Email, SMS, InPerson }
public enum ContactOutcome { Contacted, NoAnswer, InvalidContact, CustomerRefused }
public enum PromiseStatus { Pending, Kept, Broken }
```

---

## Authorization Model

### New Permission

- `collections:manage` — grants write access to collections operations

### Policy Extension

**v76**: `CanViewCollections` → `Admin`, `Operator` + `collections:view`

**v77**: `CanManageCollections` → `Admin`, `Operator` + `collections:manage`

**Separation rationale**: Some operators can view but not mutate (read-only access for reports). Mutation requires explicit permission.

---

## API Design

### New Endpoints

| Endpoint | Method | Policy | Description |
|----------|--------|--------|-------------|
| `/api/collections/delinquencies/{id}/contacts` | POST | `CanManageCollections` | Register contact attempt |
| `/api/collections/delinquencies/{id}/contacts` | GET | `CanViewCollections` | List contact history |
| `/api/collections/delinquencies/{id}/promises` | POST | `CanManageCollections` | Record payment promise |
| `/api/collections/delinquencies/{id}/promises` | GET | `CanViewCollections` | List promises |
| `/api/collections/delinquencies/{id}/notes` | POST | `CanManageCollections` | Add internal note |
| `/api/collections/delinquencies/{id}/notes` | GET | `CanViewCollections` | List notes |

**Optional (defer to v78?)**:
| `/api/collections/delinquencies/{id}/resolve` | POST | `CanManageCollections` | Manually mark as resolved |
| `/api/collections/delinquencies/{id}/escalate` | POST | `CanManageCollections` | Escalate to next level |

---

## Frontend Changes

### New Components

- **`delinquency-detail.component`**: Displays single delinquency record with tabs:
  - Overview (existing fields from v76)
  - Contact History (list + add button)
  - Promises (list + add button)
  - Notes (list + add button)

### Modified Components

- **`delinquency-list.component`**: Add "View Details" button per row → routes to detail view

### New Routes

- `/collections/delinquencies/:id` → `delinquency-detail.component`

---

## Validation Rules

| Rule | Enforcement |
|------|-------------|
| Can't mutate `Resolved` records | Backend command validation |
| Promised amount ≤ overdue amount | Backend validation |
| Notes max 1000 chars | Backend + frontend |
| Contact channel required | Backend + frontend |
| Promise date ≥ today | Backend + frontend |

---

## Audit & Observability

- All mutations are **immutable** (contact attempts, promises, notes) → full audit trail
- Log all mutations with structured logging: `DelinquencyRecordId`, `UserId`, `Operation`, `Timestamp`
- Metrics: count of contact attempts by channel, promise fulfillment rate (future)

---

## Kafka Integration Events (Optional — Assess Risk)

**Consideration**: Should we publish Kafka events when collections actions happen?

**Use cases**:
- Notify IsoSwitch to adjust authorization rules for severely delinquent accounts
- Trigger external CRM or dialer integration
- Feed analytics for collections performance

**Recommendation**: **Defer to v78**. v77 should focus on CRUD operations; Kafka integration adds complexity and requires cross-service testing.

---

## Migration Strategy

### Backend

1. Add new entities: `ContactAttemptEntity`, `PaymentPromiseEntity`, `DelinquencyNoteEntity`
2. EF migration: add tables `contact_attempts`, `payment_promises`, `delinquency_notes`
3. Add commands: `RegisterContactAttemptCommand`, `RecordPaymentPromiseCommand`, `AddDelinquencyNoteCommand`
4. Add queries: `GetContactAttemptsQuery`, `GetPaymentPromisesQuery`, `GetDelinquencyNotesQuery`
5. Extend `DelinquencyController` with new POST/GET endpoints
6. Add `CanManageCollections` policy

### Frontend

1. Create `delinquency-detail` component with tabbed layout
2. Create forms: contact attempt, payment promise, internal note
3. Update `delinquency-list` to link to detail view
4. Extend `delinquency.service` with POST/GET methods

---

## Minimal Viable Scope for v77

To keep this slice reviewable and testable, recommend:

**Phase 1 (v77)**: Contact Attempts + Notes
- ✅ Register contact attempt (POST)
- ✅ List contact history (GET)
- ✅ Add internal note (POST)
- ✅ List notes (GET)
- ✅ Backend auth + validation
- ✅ Frontend detail view + forms

**Phase 2 (v78)**: Payment Promises + Status Transitions
- Record payment promise
- Auto-check promise status (background worker)
- Manual resolution
- Escalation workflow

**Rationale**: Contact attempts + notes are simpler (no date-based state transitions), provide immediate value, and keep v77 under 400-line review budget.

---

## Open Questions

1. **Do we need a separate `DelinquencyDetailDto` that includes contact/promise/note counts?**
   - Recommendation: Yes — extend existing DTO with aggregate counts to avoid N+1 queries in list view.

2. **Should contact attempts be editable?**
   - Recommendation: No — immutable for audit integrity.

3. **Should we track "next action" or "follow-up date" on the delinquency record itself?**
   - Recommendation: Yes — add `NextActionDate` and `NextActionType` (nullable) to `DelinquencyRecordEntity`. Operator sets it when recording contact/promise.

4. **Should we enforce "can't add contact if last contact was today"?**
   - Recommendation: No — operator might call multiple times. Let business rules evolve.

---

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Scope creep into payment promises / resolution | Medium | Explicitly defer to v78 in proposal |
| Auth policy drift (view vs manage) | Low | Test both policies with integration tests |
| Frontend detail view becomes too complex | Medium | Start with tabbed layout, defer inline editing |
| Migration breaks existing v76 read-only flow | Low | Keep `DelinquencyController.GetDelinquencies` untouched |

---

## Recommendation

**Proceed with Phase 1 scope**: Contact Attempts + Internal Notes.

**Next step**: Write proposal with explicit scope boundaries, then spec deltas for `delinquency-management` capability.
