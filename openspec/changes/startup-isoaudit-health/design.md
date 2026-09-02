# Design: IsoAudit Startup Health

## Technical Approach

Keep the fix inside IsoAudit and local compose orchestration. IsoAudit will expose `/health/live` and `/health/ready`, with `/health` kept as a readiness alias. The HTTP host must finish building and mapping endpoints before dependency work runs: database migration/creation and Kafka/consumer checks move into hosted background services that update a bounded `IsoAuditReadinessState`. A transient database or Kafka outage can make readiness `starting`/`unready`, but it must not crash the process or hide diagnostics behind a startup gate.

## Architecture Decisions

| Topic | Decision | Tradeoff / Rationale |
|---|---|---|
| Startup DB policy | Remove inline `EnsureCreatedAsync`/`MigrateAsync` before endpoint mapping. Add `IsoAuditDatabaseInitializerWorker` that retries with cancellation, applies current dev/test `EnsureCreated` vs production `Migrate` policy, and marks `database` ready/failed. | Preserves safe migration behavior while liveness/readiness remain reachable during dependency startup. |
| Health contract | Keep `/health` as readiness compatibility alias, add `/health/live` for process liveness and `/health/ready` for dependency readiness. | Avoids breaking existing callers while giving Docker a real readiness signal. |
| State ownership | Add IsoAudit-local `IsoAuditReadinessState` singleton updated by database initializer, Kafka probe, and `IsoAuditConsumerWorker`. | No cross-service sync calls; IsoAudit owns dependency classification. |
| Kafka topic verification | Use `AdminClient.GetMetadata(timeout)` for all topics, then search the returned topic list for `sw.iso.audit`; do not call topic-specific metadata APIs. Configure consumer/probe clients with auto-topic creation disabled where the Confluent client option is available. | All-topic metadata is non-mutating even when the broker has auto-create enabled; missing topic becomes an explicit readiness failure. |
| Bounded retry | Configure `IsoAudit:Readiness` grace, probe interval, DB timeout, Kafka metadata timeout, and required topic. | Local dependencies can recover without turning permanent failure into fake health. |
| Diagnostics | Return only `status`, `service`, and per-check `{name,status,reason}`. | PCI-safe: no connection strings, JWT keys, message payloads, PAN/PIN/PII, or stack traces. |

## Data Flow

```text
HTTP host starts ──maps endpoints──> /health/live is queryable
        │
        ├─ Db initializer worker: EnsureCreated/Migrate with bounded retry ─┐
        ├─ Kafka readiness probe: all-topic metadata, non-mutating lookup ──┼─> IsoAuditReadinessState
        └─ Consumer worker: subscribed/running/error state ─────────────────┘

Docker healthcheck ──GET /health/ready──> readiness snapshot -> 200 ready or 503 starting/unready
IsoSwitch publishes sw.iso.audit ──Kafka──> IsoAuditConsumerWorker ──EF──> iso_message_logs
```

## File Changes

| File | Action | Description |
|---|---|---|
| `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs` | Modify | Register readiness services, remove blocking DB bootstrap before endpoints, map health endpoints, wire worker state updates. |
| `backend/services/IsoAudit/src/IsoAudit.Api/Health/IsoAuditReadinessState.cs` | Create | Thread-safe readiness snapshot and check result model. |
| `backend/services/IsoAudit/src/IsoAudit.Api/Health/IsoAuditReadinessService.cs` | Create | Runs database/Kafka/topic probes with bounded timeout and non-mutating all-topic metadata checks. |
| `backend/services/IsoAudit/src/IsoAudit.Api/Health/IsoAuditReadinessOptions.cs` | Create | Typed options for grace, probe interval, DB timeout, metadata timeout, and required topic. |
| `backend/deploy/docker-compose.yml` | Modify | Add IsoAudit healthcheck using `/health/ready`; keep `depends_on` limited to local infra health. |
| `backend/services/IsoAudit/tests/IsoAudit.Tests/Health/*` | Create | Readiness/liveness, startup resilience, missing-topic, and diagnostic safety tests. |

## Interfaces / Contracts

```csharp
public sealed record IsoAuditCheckStatus(string Name, string Status, string? Reason);
public interface IIsoAuditReadinessState
{
    IsoAuditReadinessSnapshot GetSnapshot();
    void Mark(string name, string status, string? reason = null);
}
```

HTTP: `GET /health/live` returns 200 alive. `GET /health/ready` and `GET /health` return 200 only when required checks are ready; otherwise 503 with safe check reasons.

## Failure Behavior

Startup DB failures are caught by the initializer worker, logged with safe dependency names, and reflected as `database: starting/unready`; endpoints stay reachable. Kafka reachable with missing `sw.iso.audit` is `kafka-topic: missing` after an all-topic metadata result omits the topic. Broker metadata timeout is `kafka: unavailable`. Consumer exceptions set `consumer: failed` but the host keeps running. No readiness probe calls CardVault or IsoSwitch synchronously.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit | State transitions, option defaults, non-mutating topic lookup, PCI-safe serialization | xUnit + FluentAssertions/NSubstitute. |
| Integration | Host starts when DB/Kafka probes fail; `/health/live`, `/health/ready`, `/health` status/body | `IsoAuditWebApplicationFactory`; replace probes/hosted services with test doubles. |
| Compose/manual | Docker healthcheck waits for readiness and reports missing topic/dependency | Verify with local compose; do not require real card transaction success. |

## Migration / Rollout

No data migration required. Rollback is reverting IsoAudit health code/config and compose healthcheck lines.

## Open Questions

None.

## Final Readiness Contract

IsoAudit now exposes three unauthenticated, PCI-safe health endpoints:

- `GET /health/live` returns HTTP 200 with `{ status: "alive", service: "IsoAudit.Api" }` and does not include dependency details.
- `GET /health/ready` returns HTTP 200 only when `database`, `kafka`, `kafka-topic`, and `consumer` are all ready; otherwise it returns HTTP 503 with sanitized check names, statuses, and bounded reasons.
- `GET /health` is a compatibility alias for readiness and follows the same HTTP status/body as `/health/ready`.

Startup database initialization runs in `IsoAuditDatabaseInitializerWorker`, so endpoint mapping is not blocked by database creation or migrations. Kafka readiness uses all-topic metadata lookup and checks that `sw.iso.audit` is present without producing records or forcing topic creation. The audit consumer reports `starting`, `ready`, or `failed` through the local readiness state without exposing message payloads, stack traces, or configuration values.

## Troubleshooting Notes

- `database` not ready: PostgreSQL may still be starting, migrations may be unavailable, or the configured database is unreachable. Check service logs and database health; do not expect `/health/live` to fail for this condition.
- `kafka` not ready: broker metadata could not be retrieved within the bounded timeout. Check the local Kafka container health and bootstrap address.
- `kafka-topic` not ready: Kafka is reachable but `sw.iso.audit` was not found in the all-topic metadata result. Create/verify the topic explicitly rather than relying on readiness to auto-create it.
- `consumer` not ready: the audit consumer has not subscribed yet or failed during subscribe/consume/store. Inspect IsoAudit logs; readiness diagnostics intentionally avoid payloads, secrets, PAN, PIN, and stack traces.
