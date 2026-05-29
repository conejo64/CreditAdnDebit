# Identity And Access Specification — v77 Delta

## Purpose

Extends the access control model with write-side authorization for collections management, separating read (`collections:view`) from write (`collections:manage`) permissions.

## Requirements

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

### CanViewCollections (Unchanged)

**Policy**: `CanViewCollections` (from v76)  
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

## Out of Scope

- Payment promise authorization (deferred to v78)
- Manual resolution authorization (deferred to v78)
- Escalation workflow authorization (deferred to v78)
