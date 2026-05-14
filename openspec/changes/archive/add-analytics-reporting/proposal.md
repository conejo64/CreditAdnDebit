# Proposal: Add Analytics Reporting

## Why

Sprint 9 still has `v75` pending for business analytics. CardVault already concentrates issuer, ledger, billing, settlement, disputes, and notification data, but it does not yet expose a PCI-safe reporting layer for business and risk teams.

## Scope

Add CardVault-managed analytics reporting with:

- portfolio summary KPIs for issuer operations
- consumption analytics grouped by supported business dimensions
- fraud trend analytics from dispute and chargeback activity
- graph-ready API responses for dashboards
- read-only access control and analytics access audit events

## Out Of Scope

- external BI warehouse synchronization
- frontend chart implementation
- merchant MCC analytics not supported by the current data model

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `openspec/specs/`
- `funcional/Sprints.md`
