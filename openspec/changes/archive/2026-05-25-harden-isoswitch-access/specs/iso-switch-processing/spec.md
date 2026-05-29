## ADDED Requirements

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
