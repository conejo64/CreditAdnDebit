# Proposal: Add Accounting Integration

## Why

Sprint 9 still has `v65` pending for accounting integration. CardVault already posts issuer ledger entries and creates settlement batches, but it does not yet produce formal accounting journal entries or configurable event-to-account mappings for downstream accounting integration.

## Scope

Add a CardVault-managed accounting layer with:

- ledger account catalog
- event-to-accounting mappings
- journal entries and journal lines
- automatic journal generation from ledger activity and settlement batches
- operational APIs to review journal entries and manage mappings
- outbox publication for posted accounting journal events

## Out Of Scope

- direct synchronous ERP integration
- full general ledger, trial balance, or financial statements
- analytics dashboards from `v75`

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
- `openspec/specs/`
- `funcional/Sprints.md`
