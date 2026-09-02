# Proposal: IsoAudit Startup Health

## Intent

Make IsoAudit deterministic in the local compose stack. Current evidence says PostgreSQL, SQL Server, Kafka, CardVault, and IsoSwitch are reachable; IsoAudit previously crashed during dependency startup and now returns an empty/premature `/health` response while logs mention unavailable Kafka topic `sw.iso.audit`.

## Scope

### In Scope
- IsoAudit compose readiness from `main` `a1d98a4` on `codex/startup-isoaudit-health`.
- Kafka topic availability and audit-consumer readiness diagnostics for `sw.iso.audit`.
- Safe API health/readiness checks and Docker healthchecks where justified.
- Developer-facing failures that name dependency/topic/check only, never secret values.

### Out of Scope
- Broad host-vs-container startup redesign.
- Production credential policy or commercial-mode work.
- Fake external transaction success or card-flow behavior changes.
- Cross-service synchronous dependencies.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `devops-orchestration`: refine IsoAudit container readiness and healthcheck expectations.
- `event-integration-and-observability`: add Kafka topic/consumer readiness diagnostics for audit events.

## Proposal Assumptions Needing User Review
- First success is local compose readiness, not a full startup-platform rewrite.
- `sw.iso.audit` should be diagnosable even if Kafka auto-creation stays enabled.
- Health/readiness probes may be unauthenticated only when their output is non-sensitive.

## Approach

Keep service boundaries intact. Inspect IsoAudit startup ordering, EF bootstrap, Kafka subscription, topic config, and compose health behavior. Add the smallest readiness contract using ASP.NET Core health checks or equivalent hosted-service status, with structured PCI-safe diagnostics.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `backend/deploy/docker-compose.yml` | Modified | IsoAudit healthcheck/readiness behavior. |
| `backend/services/IsoAudit/src/IsoAudit.Api` | Modified | Safe health/readiness and consumer diagnostics. |
| `backend/shared/BuildingBlocks` | Possible | Reuse existing Kafka diagnostic abstractions if present. |
| `openspec/specs/devops-orchestration` | Modified | Startup readiness contract. |
| `openspec/specs/event-integration-and-observability` | Modified | Kafka diagnostics contract. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Transient Kafka startup becomes permanent unhealthy state | Med | Use bounded grace/retry and clear degraded reasons. |
| Diagnostics leak configuration | Low | Log key names, topic names, and states only. |
| Healthcheck hides crash cause | Med | Require DB/topic/consumer/API failure classification. |

## Rollback Plan

Revert this change's IsoAudit code/config and OpenSpec deltas. Existing CardVault, IsoSwitch, and Kafka compose behavior remains unchanged.

## Dependencies

- Local Docker Compose stack with healthy PostgreSQL and Kafka.
- Existing IsoAudit topic config for `sw.iso.audit`.

## Success Criteria

- [ ] IsoAudit is healthy only after API and audit-consumer dependencies are ready.
- [ ] `GET /health` no longer returns an empty/premature response once healthy.
- [ ] Kafka topic/readiness failures produce actionable PCI-safe diagnostics.
- [ ] Verification covers healthy and missing-topic/degraded cases.
