# Issuer Ledger Billing Specification

## Purpose

Describe the issuer-side account lifecycle, ledger posting, and statement behavior owned by CardVault.

## Requirements

### Requirement: Issuer Customer And Card Lifecycle
The system SHALL manage customers, accounts, and card lifecycle operations inside CardVault.

#### Scenario: A new card is issued for an account
- WHEN an authorized operator issues a card for a customer account
- THEN CardVault stores issuer data, associates the card with the account, and returns safe card references

### Requirement ILB-CL-1: Card Lifecycle MUST Be Complete — Four Operations

The system SHALL support all four card lifecycle operations. No lifecycle action that was publicly advertised as complete (per `funcional/Sprints.md` Sprint 3) may be left as a stub or missing endpoint.

The four operations are:
1. **Block** — already implemented; remains in scope for regression assertion only.
2. **Unblock** — MUST be implemented.
3. **Cancel** — MUST be implemented.
4. **Replace** — MUST be implemented.

Each operation MUST:
- Be exposed as a dedicated `POST` endpoint under `/api/issuer/cards/{id}/<action>`.
- Be protected by the `CanOperateIssuer` authorization policy.
- Reject callers without `CanOperateIssuer` with HTTP 403 Forbidden.
- Emit a named domain audit event upon success.
- Return 204 No Content on success (except Replace, which returns 201 with `newCardId`).

#### Scenario ILB-CL-1-S1: Block (regression) — existing behavior preserved
- GIVEN an authorized operator with `CanOperateIssuer`
- WHEN they POST to `/api/issuer/cards/{id}/block`
- THEN the card status changes to Blocked
- AND a `CardBlockedEvent` is emitted
- AND the response is 204

#### Scenario ILB-CL-1-S2: Unblock — card transitions from Blocked to Active
- GIVEN an authorized operator with `CanOperateIssuer`
- AND the target card is currently in Blocked status
- WHEN they POST to `/api/issuer/cards/{id}/unblock`
- THEN the card status changes to Active
- AND a `CardUnblockedEvent` is emitted
- AND the response is 204

#### Scenario ILB-CL-1-S3: Cancel — card is permanently deactivated
- GIVEN an authorized operator with `CanOperateIssuer`
- AND the target card is in an active or blocked state
- WHEN they POST to `/api/issuer/cards/{id}/cancel`
- THEN the card status changes to Cancelled (terminal state)
- AND a `CardCancelledEvent` is emitted
- AND the response is 204

#### Scenario ILB-CL-1-S4: Replace — old card cancelled, new card issued under same account
- GIVEN an authorized operator with `CanOperateIssuer`
- AND the target card is in an active or blocked state (not already cancelled)
- WHEN they POST to `/api/issuer/cards/{id}/replace`
- THEN the old card status changes to Cancelled
- AND a new card is created under the SAME account with a new PAN token
- AND a `CardReplacedEvent` is emitted containing both the old card id and the new card id
- AND the audit record links the old card id to the new card id bidirectionally
- AND the response is 201 Created with body `{ "newCardId": "<uuid>" }`

#### Scenario ILB-CL-1-S5: Unauthorized caller is rejected with 403
- GIVEN a caller who is authenticated but does NOT hold the `CanOperateIssuer` policy
- WHEN they attempt any card lifecycle operation (unblock, cancel, or replace)
- THEN the system returns HTTP 403 Forbidden
- AND no state change occurs
- AND no domain event is emitted

#### Scenario ILB-CL-1-S6: Replace on already-cancelled card returns 409
- GIVEN an authorized operator with `CanOperateIssuer`
- AND the target card status is Cancelled
- WHEN they POST to `/api/issuer/cards/{id}/replace`
- THEN the system returns HTTP 409 Conflict
- AND no new card is created

### Requirement ILB-CL-2: Replace MUST Maintain Audit Linkage Between Cards

When a card is replaced:
1. The old card's audit record SHALL reference the new card id as its successor.
2. The new card's audit record SHALL reference the old card id as its predecessor.
3. The `CardReplacedEvent` domain event SHALL carry both `oldCardId` and `newCardId` as explicit fields.
4. The new card SHALL be issued under the SAME account — account association MUST NOT change during replacement.
5. The new card SHALL have a distinct PAN token (new tokenized card number) — the old PAN token is NOT reused.

#### Scenario ILB-CL-2-S1: Audit trail is bidirectional after replace
- GIVEN a card replacement has completed successfully
- WHEN an auditor queries the audit log for the old card id
- THEN the audit record shows the card was cancelled and replaced by `newCardId`
- WHEN the auditor queries the audit log for the new card id
- THEN the audit record shows the card was issued as a replacement for `oldCardId`

#### Scenario ILB-CL-2-S2: New card inherits account, not PAN
- GIVEN a card replacement has completed
- WHEN the new card record is inspected
- THEN its `accountId` matches the old card's `accountId`
- AND its PAN token is a freshly generated value distinct from the old card's PAN token

### Requirement ILB-CL-3: Domain Audit Events MUST Be Emitted for Every Lifecycle Transition

Each of the four lifecycle operations SHALL emit one named domain event after the state change is committed. Events are:

| Operation | Event Name | Required Fields |
|-----------|-----------|-----------------|
| Block | `CardBlockedEvent` | `cardId`, `operatorId`, `timestamp` |
| Unblock | `CardUnblockedEvent` | `cardId`, `operatorId`, `timestamp` |
| Cancel | `CardCancelledEvent` | `cardId`, `operatorId`, `timestamp`, `reason` (optional) |
| Replace | `CardReplacedEvent` | `oldCardId`, `newCardId`, `operatorId`, `timestamp`, `reason` (optional) |

#### Scenario ILB-CL-3-S1: Event is emitted after each successful lifecycle action
- GIVEN each lifecycle endpoint (block, unblock, cancel, replace) completes successfully
- THEN exactly one corresponding domain event is emitted per operation
- AND the event carries the required fields listed above

#### Scenario ILB-CL-3-S2: No event is emitted when the operation is rejected (403, 404, 409)
- GIVEN a lifecycle operation that fails (unauthorized, not found, or conflict)
- THEN NO domain event is emitted
- AND the audit log contains no entry for the failed attempt

### Requirement: Ledger Posting
The system SHALL maintain signed ledger entries for credit and debit account activity.

#### Scenario: Financial events affect account balance
- WHEN a purchase, payment, fee, or interest posting is recorded
- THEN CardVault writes ledger entries that contribute to the account balance calculation

### Requirement: Statement Generation
The system SHALL generate billing statements using ledger activity and product policy configuration.

#### Scenario: Minimum payment is derived from policy
- WHEN a statement is generated for a billing cycle
- THEN CardVault computes the minimum payment according to the configured policy and caps it to the outstanding balance when required

### Requirement: Billing Policies Remain Configurable
The system MUST allow administrative configuration of product and billing policies without moving ownership outside CardVault.

#### Scenario: Credit policy changes affect future statement generation
- WHEN an authorized administrator updates billing or credit policy settings
- THEN future calculations use the updated policy values
