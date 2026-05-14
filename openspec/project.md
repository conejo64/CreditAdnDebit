# Project Context

## Overview

ZitronSystem is a banking platform workspace whose primary active scope is the backend solution in `backend/`. The backend is organized around microservice-style bounded contexts for card issuing, vault and identity operations, and ISO 8583 switch processing.

The current source of truth for runtime behavior lives in code plus the OpenSpec capability specs under `openspec/specs/`. Future changes should treat those specs as the stable contract layer for brownfield work.

## Current Services

### CardVault

- Hosts identity and JWT-based access control
- Owns tokenization and vault key rotation workflows
- Owns issuer data such as customers, accounts, and cards
- Owns billing, ledger, disputes, settlement, and related audit streams
- Uses PostgreSQL for domain data and SQL Server for ASP.NET Core Identity data
- Publishes integration events through an EF-backed outbox to Kafka

### IsoSwitch

- Exposes ISO 8583 demo and gateway endpoints
- Resolves routing decisions and dispatches messages through connector abstractions
- Stores switch-side transaction state, routing caches, catalog caches, and audit records
- Consumes CardVault-originated Kafka events to materialize local read models
- Emits transaction and ISO event streams plus telemetry

### Shared Building Blocks

- Kafka producer and consumer abstractions
- Kafka message signing and trace propagation
- Retry and DLQ helpers
- Outbox abstractions and workers
- Common result types

## Architectural Conventions

- Respect bounded contexts. CardVault is the system of record for cardholder, issuer, and vault concerns. IsoSwitch is the system of record for routing and switch execution concerns.
- Prefer asynchronous integration through Kafka topics and outbox-backed publishing.
- Avoid leaking sensitive values into logs, events, responses, or downstream stores.
- Maintain idempotency for inbound APIs and Kafka consumers where the workflow can be retried.
- Treat observability as a first-class concern. Metrics, traces, and structured logs are part of the platform contract.

## Tech Stack

- .NET 9
- ASP.NET Core Web APIs and hosted services
- Entity Framework Core
- PostgreSQL
- SQL Server Identity store
- Kafka
- Serilog
- OpenTelemetry and Prometheus
- Docker Compose for local infra

## Local Workflow

- Solution file: `backend/CardSwitchPlatform.sln`
- Infra bootstrap: `backend/deploy/docker-compose.yml`
- Swagger endpoints are exposed by CardVault and IsoSwitch in development
- Development bootstrapping currently favors fast local startup with `EnsureCreated()` in some flows and migrations in production-oriented flows

## Spec Authoring Guidance

- Add a new capability spec when behavior does not fit an existing bounded context cleanly.
- Prefer modifying an existing spec instead of creating near-duplicate capability names.
- Write requirements with SHALL or MUST language.
- Each requirement should include at least one concrete scenario.
- Keep specs focused on externally meaningful behavior, contracts, policies, and ownership boundaries rather than internal helper classes.

## Out Of Scope By Default

- Frontend-only design changes unless the task explicitly targets `frontend/`
- Rewriting service boundaries without a proposal and design document
- Direct cross-service calls that bypass Kafka or outbox durability
