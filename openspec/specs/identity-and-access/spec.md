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

### Requirement: Collections Visibility Policy

The system MUST enforce a specific authorization policy (`CanViewCollections`) to restrict access to early delinquency and collections data.

#### Scenario: Operator with collections role accesses the delinquency list

- GIVEN a user assigned a role with the `CanViewCollections` policy
- WHEN the user accesses the delinquency management API
- THEN the request is authorized

#### Scenario: Operator without collections role is denied

- GIVEN a user who is authenticated but lacks the `CanViewCollections` policy
- WHEN the user accesses the delinquency management API
- THEN the request is denied with a 403 Forbidden

### Requirement: Collections Management Authorization

The system SHALL enforce granular authorization for collections write operations, distinguishing between read-only access and mutation capabilities.

#### Scenario: User with collections:manage can mutate

- GIVEN a user with the `collections:manage` permission
- WHEN the user attempts to register a contact attempt or add an internal note
- THEN the system authorizes the request
- AND allows the mutation to proceed

#### Scenario: User with only collections:view cannot mutate

- GIVEN a user with only the `collections:view` permission (read-only)
- WHEN the user attempts to register a contact attempt or add an internal note
- THEN the system rejects the request with HTTP 403 Forbidden
- AND returns an authorization error message

#### Scenario: Admin and Operator roles can manage collections

- GIVEN a user with the `Admin` OR `Operator` role
- AND no explicit `collections:manage` permission (falls back to role)
- WHEN the user attempts a collections write operation
- THEN the system authorizes the request based on role membership
- AND allows the mutation to proceed

#### Scenario: Auditor role excluded from write operations

- GIVEN a user with the `Auditor` role
- AND no explicit `collections:manage` permission
- WHEN the user attempts a collections write operation
- THEN the system rejects the request with HTTP 403 Forbidden
- AND the user can still view collections data (read-only via `collections:view`)

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

## Authorization Policies

### CanManageCollections

**Policy**: `CanManageCollections`  
**Authorized**:
- Users with role `Admin`
- Users with role `Operator`
- Users with explicit claim `collections:manage`

**Excluded**:
- `Auditor` role (read-only via `CanViewCollections`)
- Users without role or explicit permission

### CanViewCollections

**Policy**: `CanViewCollections`  
**Authorized**:
- Users with role `Admin`
- Users with role `Operator`
- Users with role `Auditor`
- Users with explicit claim `collections:view`

## Permission Catalog

**New permission**: `collections:manage`  
**Description**: Grants write access to collections operations (contact attempts, notes, and future mutation actions)

## Endpoints Protected

| Endpoint | Method | Policy |
|----------|--------|--------|
| `/api/collections/delinquencies/{id}/contacts` | POST | `CanManageCollections` |
| `/api/collections/delinquencies/{id}/contacts` | GET | `CanViewCollections` |
| `/api/collections/delinquencies/{id}/notes` | POST | `CanManageCollections` |
| `/api/collections/delinquencies/{id}/notes` | GET | `CanViewCollections` |
