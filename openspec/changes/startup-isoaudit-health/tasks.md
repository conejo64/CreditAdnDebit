# Tasks: IsoAudit Startup Health

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 300-500 |
| 400-line budget risk | Medium |
| Chained PRs recommended | No |
| Suggested split | Single PR: tests → health state/probes → Program/compose wiring → docs |
| Delivery strategy | single-pr authorized |
| Chain strategy | pending |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Medium

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Startup health core | PR 1 | readiness state, bounded probes, consumer/db diagnostics |
| 2 | Host/compose wiring | PR 1 | Program.cs + docker-compose healthcheck |
| 3 | Verification/docs | PR 1 | integration tests and runbook updates |

## Phase 1: RED — Health contract tests
- [x] 1.1 Add failing tests in `backend/services/IsoAudit/tests/IsoAudit.Tests/Health/*` for `/health/live`, `/health/ready`, and `/health` returning safe, non-sensitive payloads.
- [x] 1.2 Add failing tests for bounded startup states: DB unready, Kafka topic `sw.iso.audit` missing, consumer not started, and readiness timeout behavior.
- [x] 1.3 Add failing tests proving no synchronous cross-service call and no secret/PII leakage in diagnostics.

## Phase 2: GREEN — Readiness state and probes
- [x] 2.1 Create `backend/services/IsoAudit/src/IsoAudit.Api/Health/IsoAuditReadinessState.cs` plus options/probe types for database, Kafka, and consumer status.
- [x] 2.2 Implement bounded DB initializer and Kafka all-topic metadata probe in `backend/services/IsoAudit/src/IsoAudit.Api/Health/*`; keep checks non-mutating except DB init policy.
- [x] 2.3 Update `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs` to stop blocking startup, register hosted services, and map `/health/live`, `/health/ready`, `/health`.

## Phase 3: Wiring — Compose and diagnostics
- [x] 3.1 Update `backend/deploy/docker-compose.yml` so IsoAudit healthcheck calls `/health/ready` and depends only on local infra health.
- [x] 3.2 Wire consumer startup/error reporting from `IsoAuditConsumerWorker` into readiness state without exposing payloads or secrets.

## Phase 4: Verify — Tests and docs
- [x] 4.1 Add integration tests for healthy startup, missing Kafka topic, and degraded DB/consumer scenarios in `backend/services/IsoAudit/tests/IsoAudit.Tests/Health/*`.
- [x] 4.2 Update the change docs under `openspec/changes/startup-isoaudit-health/` to reflect the final readiness contract and troubleshooting notes.
