# AGENTS.md

This repository uses OpenSpec-style spec-driven development for substantive changes.

## Default Workflow

1. Read [openspec/project.md](/d:/Jonathan/desarrollo/ZitronSystem/openspec/project.md) and the relevant files under [openspec/specs](/d:/Jonathan/desarrollo/ZitronSystem/openspec/specs) before changing behavior.
2. For any user-visible feature, contract, data-model, integration, or policy change, create an OpenSpec change folder under `openspec/changes/<change-slug>/` before editing code.
3. In each change folder, add:
   - `proposal.md` for scope and rationale
   - `tasks.md` for the implementation checklist
   - `design.md` only when the technical tradeoffs are non-trivial
   - spec deltas under `specs/<capability>/spec.md`
4. Implement only after the proposal and spec delta are aligned with the requested outcome.
5. After implementation, update the task checklist and merge approved deltas back into `openspec/specs/`, then archive the change under `openspec/changes/archive/`.

Small refactors, typo fixes, or non-behavioral maintenance can skip the proposal step, but they should still respect the current specs and project context.

## Repository Scope

- Primary scope: banking backend services under `backend/`
- Secondary scope: frontend under `frontend/` only when a change explicitly crosses the API/UI boundary
- Current backend runtime: .NET 9, ASP.NET Core, Entity Framework Core, PostgreSQL, SQL Server Identity, Kafka, OpenTelemetry, Serilog

## Standing Review Roles

Every repository review, architecture analysis, and implementation plan must be evaluated through these standing roles:

- **Senior Architect**: validate bounded contexts, dependency direction, transactional boundaries, scalability, resilience, observability, and long-term maintainability.
- **Senior .NET Backend Developer**: validate ASP.NET Core, EF Core, async flows, dependency injection, testing, security posture, configuration, and production readiness.
- **Ecuador Card Payments Domain Expert**: evaluate credit/debit card flows against Ecuador banking realities, including issuer/acquirer separation, cardholder notification evidence, PCI-safe handling, SBS/BCE-facing auditability, settlement, disputes, delinquency, and operational controls.

When these roles disagree, prefer the safer banking-platform decision: preserve PCI boundaries, favor auditable asynchronous workflows, avoid fake success states, and require explicit OpenSpec/SDD rationale before weakening controls.

## Banking Backend Guardrails

- CardVault owns identity, tokenization, issuer data, ledger, billing, dispute, settlement, and PCI-sensitive operations.
- IsoSwitch owns ISO 8583 request handling, routing decisions, connector execution, transaction state, and switch-side audit materialization.
- Prefer asynchronous integration through Kafka and the outbox pattern. Do not introduce synchronous service-to-service dependencies across bounded contexts.
- Preserve PCI boundaries. Never expose raw PAN, PIN, or sensitive PII across service boundaries or logs.
- Consumers and retry flows must remain idempotent.
- Kafka message signing, trace propagation, and retry or DLQ behavior are part of the platform contract and should not be bypassed.
- Schema changes must consider migrations, event compatibility, and existing audit trails.

## Key Paths

- `backend/CardSwitchPlatform.sln`
- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/IsoSwitch/src/IsoSwitch.Api`
- `backend/shared/BuildingBlocks`
- `backend/deploy/docker-compose.yml`
- `backend/observability`

## Review Focus

When reviewing or implementing changes, explicitly check:

- service boundary ownership
- outbox or Kafka durability
- idempotency and compensating behavior
- PCI-safe logging and audit records
- authorization and role boundaries
- traceability through logs, metrics, and trace context
