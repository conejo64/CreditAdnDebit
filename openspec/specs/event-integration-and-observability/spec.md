# Event Integration And Observability Specification

## Purpose

Describe the shared eventing, durability, and observability contracts that connect CardVault and IsoSwitch.

## Requirements

### Requirement: Durable Event Publication
The system SHALL publish integration events through durable outbox-backed workflows rather than direct fire-and-forget messaging.

#### Scenario: CardVault publishes integration data
- WHEN CardVault commits domain data that requires downstream propagation
- THEN the event is stored durably and published through the outbox workflow

### Requirement: Local Materialized Read Models
The system SHALL let IsoSwitch consume CardVault-originated events to maintain local caches and read models.

#### Scenario: Routing or catalog updates propagate asynchronously
- WHEN CardVault publishes routing or catalog changes
- THEN IsoSwitch consumes those events and updates its local persistence without relying on synchronous CardVault calls

### Requirement: Secure And Traceable Kafka Messaging
The system MUST apply message signing and trace propagation to Kafka-based integration flows.

#### Scenario: Signed message headers are validated by consumers
- WHEN a protected Kafka message is consumed
- THEN the consumer verifies the configured signature policy before treating the payload as valid

#### Scenario: Trace context survives cross-service messaging
- WHEN a request publishes downstream Kafka events
- THEN the event headers carry trace context that allows correlated tracing in consumers and telemetry backends

### Requirement: Retry, DLQ, And Metrics Support
The system SHALL expose retry or DLQ handling and telemetry for Kafka failure management.

#### Scenario: Consumer failures exhaust retry policy
- WHEN a consumer cannot process a message within the configured retry policy
- THEN the system moves the message into the configured retry or dead-letter flow and records the corresponding metrics

### Requirement: Service Telemetry
The system MUST expose structured logs, traces, and metrics for backend operations.

#### Scenario: Runtime telemetry is enabled
- WHEN CardVault or IsoSwitch is running with observability enabled
- THEN each service emits OpenTelemetry traces and metrics together with structured application logs
