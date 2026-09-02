# Delta for devops-orchestration

## ADDED Requirements

### Requirement: IsoAudit Safe Health Endpoints

IsoAudit MUST expose liveness and readiness endpoints that return non-sensitive responses and never report healthy before the service is actually ready.

#### Scenario: Liveness is safe
- GIVEN IsoAudit is running
- WHEN a caller requests the liveness endpoint
- THEN the response MUST indicate the process is alive
- AND the body MUST NOT include secrets, PAN, PIN, or dependency details

#### Scenario: Readiness blocks premature health
- GIVEN the API has started but a required dependency is still unavailable
- WHEN a caller requests the readiness endpoint
- THEN the response MUST be unhealthy or degraded
- AND the response MUST NOT claim the service is healthy

### Requirement: Docker Readiness Respects Dependency Classification

Docker healthchecks for IsoAudit MUST rely on the safe readiness contract and MUST remain unhealthy until the service has classified database, Kafka, audit-topic, and consumer startup outcomes within a bounded grace period.

#### Scenario: Dependencies become ready in time
- GIVEN database, Kafka, the audit topic, and the consumer are all available within the configured startup grace period
- WHEN the Docker healthcheck runs after startup
- THEN the container MUST become healthy

#### Scenario: Dependency stays unavailable
- GIVEN at least one required dependency remains unavailable after bounded retries
- WHEN the Docker healthcheck runs
- THEN the container MUST remain unhealthy
- AND the status MUST reflect readiness failure rather than a falsely healthy API