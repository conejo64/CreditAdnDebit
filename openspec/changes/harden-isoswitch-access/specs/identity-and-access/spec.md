## ADDED Requirements

### Requirement: Protected IsoSwitch Operational Access
The system MUST require a valid CardVault-issued bearer token for operational IsoSwitch APIs.

#### Scenario: Switch monitor and audit access require an authenticated role
- WHEN a caller requests switch transactions, ISO logs, or audit records from `IsoSwitch.Api`
- THEN the request is accepted only when the caller presents a valid JWT issued for the platform audience
- AND the caller must satisfy the configured monitor or audit authorization policy

#### Scenario: Switch execution endpoints require operator or admin authority
- WHEN a caller invokes protected ISO transaction execution endpoints such as authorization, capture, reversal, or network management
- THEN `IsoSwitch.Api` rejects anonymous callers
- AND the caller must satisfy the switch-operation authorization policy
