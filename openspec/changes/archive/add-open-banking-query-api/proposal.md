# Proposal: Add Open Banking Query API

## Why

Sprint 9 still has `v73` pending for Open Banking. The backend already owns issuer account data, ledger movements, audit trails, and JWT signing, but it does not expose a dedicated external API for authorized third-party clients to query balances and transactions.

## Scope

Add a CardVault-managed Open Banking query capability with:

- OAuth-style client credentials token issuance for external clients
- scope-based access to balance and transaction query APIs
- per-client authorization to specific accounts or all accounts
- audit logging for token issuance and data access
- admin endpoints to register Open Banking clients and grant account access

## Out Of Scope

- customer consent UX and revocation workflows
- PSD2 redirect flows or user-delegated authorization
- payment initiation APIs
- frontend integrations

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
- `openspec/specs/`
- `funcional/Sprints.md`
