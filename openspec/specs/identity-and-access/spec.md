# Identity And Access Specification

## Purpose

Define how banking operators and administrators authenticate to CardVault and how access is constrained across protected backend capabilities.

## Requirements

### Requirement: JWT-Based Authentication
The system SHALL authenticate valid users through CardVault and issue bearer-token based access for protected APIs.

#### Scenario: Valid login establishes an authenticated session
- WHEN an enabled user submits valid credentials to the CardVault authentication flow
- THEN CardVault returns an access token for subsequent authorized API calls

#### Scenario: Seeded development users are available
- WHEN the backend starts in development with an empty identity store
- THEN CardVault seeds the default operator roles and default administrative users needed for local testing

### Requirement: Role-Based Authorization
The system SHALL enforce role-based authorization policies for operational banking actions.

#### Scenario: Administrative actions require elevated role membership
- WHEN a caller invokes admin-only capabilities such as vault key rotation or user-role management
- THEN the request is allowed only for users that satisfy the configured administrative policy

#### Scenario: Audit and read-only access is narrower than write access
- WHEN an auditor or read-focused role calls a protected endpoint
- THEN the system grants only the policies explicitly intended for that role

### Requirement: Permission-Based Sensitive Access
The system MUST support claims-based permission checks for especially sensitive operations.

#### Scenario: Detokenization requires explicit permission or admin authority
- WHEN a caller requests a detokenization operation
- THEN the caller must be an administrator or hold the `vault:detokenize` permission claim
