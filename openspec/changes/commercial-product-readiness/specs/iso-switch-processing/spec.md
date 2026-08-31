# Delta for Iso Switch Processing

## MODIFIED Requirements

### Requirement: Explicit Separation Between Operational And Demo Endpoints
The system SHALL keep protected workflows distinct from demo or diagnostic helpers. Demo, simulator, and diagnostic helpers MUST be unavailable in commercial mode; availability requires explicit demo mode and synthetic or tokenized inputs. They MUST NOT establish real-rail, live-switching, or production-payment readiness.

(Previously: Demo helpers were required to be identifiable and separate from protected operational routes.)

#### Scenario: Operational ISO processing does not rely on anonymous demo routes
- WHEN a caller executes an operational switch workflow through `/api/iso/*`, `/api/transactions*`, `/api/audit/*`, `/api/catalog/*`, or `/api/routing/*`
- THEN the workflow is governed by explicit authorization policies
- AND access is not granted through anonymous demo-only routes

#### Scenario: Demo helpers remain clearly identifiable
- WHEN IsoSwitch exposes local diagnostic or simulator helper routes
- THEN those routes remain under explicit demo or simulator route prefixes or dedicated helper paths
- AND they stay separated from the protected operational route surface

#### Scenario: Commercial mode denies simulator helpers
- GIVEN IsoSwitch runs in commercial mode
- WHEN a caller requests a simulator or demo helper
- THEN the route is not mapped or returns an unavailable response
- AND no transaction, audit mutation, or event publication occurs
