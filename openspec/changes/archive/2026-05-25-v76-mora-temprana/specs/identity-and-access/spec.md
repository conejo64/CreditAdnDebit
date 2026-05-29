# Delta for Identity And Access

## ADDED Requirements

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
