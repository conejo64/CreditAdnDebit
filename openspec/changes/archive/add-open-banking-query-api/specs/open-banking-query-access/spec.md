## ADDED Requirements

### Requirement: External Client Credentials Authentication
The system SHALL allow authorized external applications to obtain access tokens for Open Banking query APIs through a client credentials flow.

#### Scenario: Valid client credentials receive an access token
- WHEN an enabled Open Banking client submits valid credentials for the allowed scopes
- THEN CardVault issues a bearer token for the configured Open Banking audience
- AND CardVault records an audit event for the token issuance

### Requirement: Scoped Account Query Authorization
The system MUST restrict Open Banking queries by scope and by account authorization.

#### Scenario: Authorized client can query transactions
- WHEN an Open Banking client with the `ob:transactions` scope requests transactions for an authorized account
- THEN CardVault returns the account transaction history

#### Scenario: Unauthorized account access is rejected
- WHEN an Open Banking client requests an account outside its authorized access grants
- THEN CardVault rejects the request

### Requirement: Balance Query Access
The system SHALL expose current balance information for authorized accounts.

#### Scenario: Authorized client can query balance
- WHEN an Open Banking client with the `ob:balances` scope requests the balance of an authorized account
- THEN CardVault returns current account balance and available limit information

### Requirement: Open Banking Audit Trail
The system MUST record audit information for Open Banking authentication and read access.

#### Scenario: Query access is audited
- WHEN an Open Banking client reads transactions or balances
- THEN CardVault stores an audit record with the client identifier, requested resource, and trace context
- AND the audit payload excludes raw PAN and client secret values
