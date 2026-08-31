# Commercial Deployment Governance Specification

## Purpose

Define truthful product claims and safe commercial deployment modes.

## Requirements

### Requirement: Feature Maturity Register

The system SHALL maintain a maturity register. Each demonstrated capability MUST be `verified`, `simulation`, or `roadmap`, with owner, permitted modes, and evidence. A capability MUST NOT be marketed as verified without evidence.

#### Scenario: Verified claim has evidence
- GIVEN a capability is presented as verified
- WHEN its register entry is reviewed
- THEN it includes retrievable verification evidence

#### Scenario: Unsupported claim is withheld
- GIVEN a capability has no required evidence
- WHEN a commercial claim is prepared
- THEN the capability is classified as simulation or roadmap

### Requirement: Commercial Mode Is Fail Closed

Commercial deployments MUST default to commercial mode. Demo mode requires explicit enablement and synthetic or tokenized data. Commercial mode MUST NOT expose demo, simulator, anonymous diagnostic, or unsupported Swagger/OpenAPI surfaces.

#### Scenario: Default commercial deployment
- GIVEN no non-commercial mode is explicitly enabled
- WHEN a service starts
- THEN demo and diagnostic routes are unavailable

#### Scenario: Isolated demo deployment
- GIVEN non-commercial demo mode is explicitly enabled
- WHEN a simulator helper is invoked with synthetic data
- THEN it is available only within its designated demo environment

### Requirement: Operator Maturity Disclosure

Operator journeys MUST identify simulated and unavailable capabilities before invocation. Simulated actions MUST state they have no financial effect; unavailable actions MUST be disabled or rejected with a clear explanation and MUST NOT report success.

#### Scenario: Simulated action disclosure
- GIVEN an operator views an enabled simulated capability
- WHEN the action is presented
- THEN the interface identifies it as simulated and non-money-moving

#### Scenario: Commercially unavailable action
- GIVEN a capability is not permitted in commercial mode
- WHEN an operator attempts to use it
- THEN the interface explains its unavailability and no operation is performed
