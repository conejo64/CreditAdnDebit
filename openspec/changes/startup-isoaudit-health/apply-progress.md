# Apply Progress: IsoAudit Startup Health

## Status
P1 startup-blocking readiness defects were corrected after fresh review. Scope was limited to consumer startup yielding, asynchronous/cancellable Kafka metadata probing, and focused regression tests. All changes remain uncommitted.

## P1 Remediation TDD Cycle Evidence
| Defect | RED | GREEN | REFACTOR |
|---|---|---|---|
| Consumer hosted service startup can enter synchronous consume loop before yielding | Added `IsoAuditConsumerWorkerStartupTests.StartAsync_YieldsBeforeEnteringBlockingConsumeLoop`; the test exercises the real hosted service instead of suppressing hosted services and failed before implementation because startup could block around the consume loop. | Added an explicit `await Task.Yield()` at the start of `IsoAuditConsumerWorker.ExecuteAsync`, followed by a cancellation check before Kafka consumer setup. | Kept the consumer state machine, safe diagnostics, topic config, and payload handling unchanged. |
| Kafka topic readiness probe performs synchronous metadata on the hosted-service startup thread | Added async/cancellation tests in `IsoAuditKafkaTopicProbeTests`; initial RED failed to compile because there was no injectable metadata reader seam and `CheckAsync` was synchronous around `GetMetadata`. | `CheckAsync` now schedules the bounded all-topic metadata lookup off the caller/startup path and awaits it with caller cancellation. | Kept non-mutating all-topic semantics and `AllowAutoCreateTopics=false`; the seam is internal and used only for focused tests. |

## Verification
- Baseline safety net: `dotnet test backend/services/IsoAudit/tests/IsoAudit.Tests/IsoAudit.Tests.csproj --filter "FullyQualifiedName~IsoAudit.Tests.Health" -m:1 --no-restore` passed: 7/7.
- RED: same command failed with CS1729 after adding async Kafka probe tests that referenced the not-yet-existing injectable metadata reader constructor.
- GREEN/REFACTOR: same command passed: 10/10.

## Changed Files In This P1 Fix
- `backend/services/IsoAudit/src/IsoAudit.Api/Program.cs` — `IsoAuditConsumerWorker.ExecuteAsync` now yields before possible blocking Kafka consume work.
- `backend/services/IsoAudit/src/IsoAudit.Api/Health/IsoAuditKafkaTopicProbe.cs` — metadata lookup now runs asynchronously from the caller perspective and observes cancellation while preserving all-topic lookup semantics.
- `backend/services/IsoAudit/tests/IsoAudit.Tests/Health/IsoAuditConsumerWorkerStartupTests.cs` — regression test for real hosted-service startup yielding.
- `backend/services/IsoAudit/tests/IsoAudit.Tests/Health/IsoAuditKafkaTopicProbeTests.cs` — regression tests for non-blocking `CheckAsync` call return and cancellation.

## Scope Guard
- No services were started.
- No push, merge, rebase, or commit was performed.
- No fake audit events, secrets, PAN, PIN, or PII were introduced.
- Existing task checklist remains complete; this file records the review remediation rather than expanding the approved task scope.
