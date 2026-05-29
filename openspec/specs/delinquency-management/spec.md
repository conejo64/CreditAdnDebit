# Delinquency Management Specification

## Purpose

Provides visibility and tracking of accounts in arrears and their respective aging buckets, along with write operations for collections workflow such as contact attempt registration and internal note tracking.

## Requirements

### Requirement: Read-Only Collections Visibility

The system SHALL expose read-only API endpoints to retrieve paginated lists of delinquent accounts with their aging buckets.

#### Scenario: Operator queries delinquent accounts

- GIVEN a user with the required collection visibility permission
- WHEN the user queries the delinquency list
- THEN the system returns a paginated list of accounts currently in arrears
- AND each account record displays the appropriate aging bucket (e.g., 1-30, 31-60 days)

#### Scenario: Unauthorized access prevention

- GIVEN a user without the required collections permission
- WHEN the user queries the delinquency list
- THEN the system rejects the request with a 403 Forbidden response

### Requirement: Collections Mutation Operations

The system SHALL provide write operations for collections management, allowing authorized operators to register contact attempts and add internal notes while maintaining audit integrity.

#### Scenario: Register contact attempt for active delinquency

- GIVEN a user with `collections:manage` permission
- AND a delinquency record in `Active` status
- WHEN the user registers a contact attempt with channel, outcome, and optional notes
- THEN the system persists the attempt with timestamp and user identity
- AND returns success confirmation
- AND the attempt appears in the contact history for that delinquency record

#### Scenario: Add internal note for active delinquency

- GIVEN a user with `collections:manage` permission
- AND a delinquency record in `Active` status
- WHEN the user adds an internal note (max 1000 characters)
- THEN the system persists the note with timestamp and user identity
- AND returns success confirmation
- AND the note appears in the notes list for that delinquency record

#### Scenario: Reject mutations for resolved delinquency records

- GIVEN a user with `collections:manage` permission
- AND a delinquency record in `Resolved` status
- WHEN the user attempts to register a contact attempt OR add a note
- THEN the system rejects the request with a validation error
- AND returns a message indicating the record is resolved and immutable

#### Scenario: List contact history

- GIVEN a user with `collections:view` permission (read-only)
- AND a delinquency record with one or more contact attempts
- WHEN the user queries the contact history for that record
- THEN the system returns all contact attempts sorted by timestamp descending
- AND each attempt includes: channel, outcome, notes, attempted by user, timestamp

#### Scenario: List internal notes

- GIVEN a user with `collections:view` permission (read-only)
- AND a delinquency record with one or more internal notes
- WHEN the user queries the notes for that record
- THEN the system returns all notes sorted by timestamp descending
- AND each note includes: content, created by user, timestamp

#### Scenario: Audit trail immutability

- GIVEN a contact attempt or internal note has been persisted
- WHEN any user attempts to edit or delete the record
- THEN the system does not expose edit or delete operations
- AND the record remains immutable for audit integrity

## Data Contracts

### Contact Attempt

```
{
  "id": "guid",
  "delinquencyRecordId": "guid",
  "channel": "Phone" | "Email" | "SMS" | "InPerson",
  "outcome": "Contacted" | "NoAnswer" | "InvalidContact" | "CustomerRefused",
  "notes": "string (optional, max 1000 chars)",
  "attemptedBy": "string (user email or ID)",
  "attemptedOn": "ISO 8601 timestamp"
}
```

### Internal Note

```
{
  "id": "guid",
  "delinquencyRecordId": "guid",
  "content": "string (max 1000 chars)",
  "createdBy": "string (user email or ID)",
  "createdOn": "ISO 8601 timestamp"
}
```
