# Delta for event-integration-and-observability

## ADDED Requirements

### Requirement: IsoAudit Dependency-Aware Readiness Diagnostics

IsoAudit MUST classify readiness outcomes for database, Kafka, the `sw.iso.audit` topic, and the audit consumer, and it MUST emit actionable diagnostics without leaking secrets or PCI data.

#### Scenario: Kafka topic is missing
- GIVEN Kafka is reachable but `sw.iso.audit` cannot be confirmed
- WHEN readiness is evaluated
- THEN the service MUST report a dependency-specific failure
- AND the diagnostic MUST name the missing topic or dependency state only

#### Scenario: Consumer fails to initialize
- GIVEN the database and Kafka are reachable but the audit consumer cannot start
- WHEN readiness is evaluated
- THEN the service MUST report the consumer as not ready
- AND the diagnostic MUST be safe for logs and HTTP responses

### Requirement: IsoAudit Startup Uses Bounded Retry Without Synchronous Cross-Service Calls

IsoAudit MUST use bounded startup grace and retry behavior for local dependencies and MUST NOT introduce synchronous cross-service calls to determine readiness.

#### Scenario: Dependency recovers during retry window
- GIVEN a startup dependency is briefly unavailable
- WHEN the bounded retry window is still open
- THEN IsoAudit MAY continue retrying readiness checks
- AND it MUST not block on any synchronous call to another service

#### Scenario: Retry window expires
- GIVEN a required dependency remains unavailable beyond the bounded retry window
- WHEN startup completes
- THEN the service MUST remain unready
- AND the failure MUST be observable through safe diagnostics