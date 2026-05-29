# Delinquency Management Specification

## Purpose

Provides visibility and tracking of accounts in arrears and their respective aging buckets without introducing mutation capabilities.

## Requirements

### Requirement: Read-Only Collections Visibility

The system SHALL expose read-only API endpoints to retrieve paginated lists of delinquent accounts with their aging buckets.

#### Scenario: Operator queries delinquent accounts

- GIVEN a user with the required collection visibility permission
- WHEN the user queries the delinquency list
- THEN the system returns a paginated list of accounts currently in arrears
- AND each account record displays the appropriate aging bucket (e.g., 1-30, 31-60 days)

#### Scenario: Enforcing read-only constraints

- GIVEN the collections module is accessed
- WHEN the user attempts any mutation action (e.g., adding contact notes)
- THEN the action is not available in the API or UI in this version

#### Scenario: Unauthorized access prevention

- GIVEN a user without the required collections permission
- WHEN the user queries the delinquency list
- THEN the system rejects the request with a 403 Forbidden response
