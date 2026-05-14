# Proposal: Add Credit Limit Management

## Why

Sprint 9 still has `v71` pending for credit limit management. CardVault already validates available credit and can optionally allow overlimit authorizations, but it does not yet enforce a controlled overlimit buffer, record approved excess usage, or evaluate and propose credit limit increases.

## Scope

Add CardVault-managed credit limit management with:

- controlled overlimit buffer validation by product policy
- persistent overlimit event tracking for approved excess usage
- fee-compatible overlimit traceability
- credit limit evaluation based on payment performance and utilization
- proposal listing and limit increase application APIs

## Out Of Scope

- external credit bureau integrations
- machine learning underwriting
- customer-facing self-service acceptance workflow

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
- `openspec/specs/`
- `funcional/Sprints.md`
