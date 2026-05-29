# Iso Switch Processing Specification

## Purpose

Define how IsoSwitch validates, routes, transmits, and records ISO 8583 transaction traffic.

## Requirements

### Requirement: ISO 8583 Transaction Handling
The system SHALL expose switch APIs that build, send, parse, and persist ISO 8583 transaction activity.

#### Scenario: Authorization requests become tracked transactions
- WHEN a caller submits an authorization request
- THEN IsoSwitch builds the ISO message, persists the transaction state, sends the message through the selected connector, and records the response outcome

#### Scenario: Utility endpoints support diagnostics
- WHEN an operator uses ISO build or parse utilities
- THEN IsoSwitch returns diagnostic message representations without bypassing the platform’s formatting rules

### Requirement: Routing Decision Ownership
The system SHALL make connector routing decisions inside IsoSwitch using locally materialized rules and catalog data.

#### Scenario: Routing uses locally cached configuration
- WHEN IsoSwitch receives an authorization or capture request
- THEN it resolves the connector decision from switch-owned routing logic and locally materialized catalog or routing caches

### Requirement: Idempotent Transaction Processing
The system MUST preserve idempotent behavior for retried switch requests.

#### Scenario: Replayed idempotency key returns existing outcome
- WHEN a caller retries a supported API request with the same idempotency key and transaction type
- THEN IsoSwitch returns the previously persisted transaction outcome instead of executing the workflow twice

### Requirement: Transaction State Integrity
The system MUST enforce allowed transaction state transitions.

#### Scenario: A response updates the transaction outcome
- WHEN IsoSwitch receives a successful or failed downstream response
- THEN it applies only a valid state transition for that transaction type before publishing the resulting switch event

### Requirement: Explicit Separation Between Operational And Demo Endpoints
The system SHALL keep protected operational switch workflows distinct from demo or diagnostic helpers.

#### Scenario: Operational ISO processing does not rely on anonymous demo routes
- WHEN a caller executes an operational switch workflow through `/api/iso/*`, `/api/transactions*`, `/api/audit/*`, `/api/catalog/*`, or `/api/routing/*`
- THEN the workflow is governed by explicit authorization policies
- AND access is not granted through anonymous demo-only routes

#### Scenario: Demo helpers remain clearly identifiable
- WHEN IsoSwitch exposes local diagnostic or simulator helper routes
- THEN those routes remain under explicit demo or simulator route prefixes or dedicated helper paths
- AND they stay separated from the protected operational route surface
