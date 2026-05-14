# Proposal: Add Customer Notifications Engine

## Why

Sprint 9 still has `v74` pending for customer notifications. The backend already owns customer contact data, switch transaction materialization, risk events, and outbox-backed publishing, but it does not yet provide a notification engine for customer-facing transaction messages or security alerts.

## Scope

Add a CardVault-managed notification capability that:

- creates transaction notifications from switch-side transaction events
- creates security alerts from suspicious authentication activity
- persists notification records and per-channel delivery state
- dispatches simulated email and SMS deliveries asynchronously
- exposes operational read APIs for notification history and delivery status

## Out Of Scope

- real SMS, push, or email provider integrations
- customer preference center and opt-in management
- frontend inbox or mobile UI work
- Open Banking and analytics backlog items

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
- `openspec/specs/`
- `funcional/Sprints.md`
