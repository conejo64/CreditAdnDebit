# Vault And Pci Specification

## Purpose

Define the security and operational rules for PAN tokenization, vault key management, and PCI-safe auditing in CardVault.

## Requirements

### Requirement: Tokenized Card Data Handling
The system SHALL keep raw PAN handling inside CardVault and return masked or tokenized representations to clients and downstream systems.

#### Scenario: Card issuance or vault workflows expose safe values
- WHEN CardVault processes card-sensitive data for issuance or vault operations
- THEN external responses expose tokenized or masked values instead of raw PAN

### Requirement: Controlled Detokenization
The system MUST protect detokenization with authorization and rate limiting.

#### Scenario: Authorized detokenization succeeds within policy
- WHEN an authorized caller with the required permission performs a detokenization request within the allowed rate window
- THEN the request is processed and audited

#### Scenario: Excessive detokenization attempts are throttled
- WHEN detokenization requests exceed the configured per-minute threshold
- THEN CardVault enforces the configured rate limiter

### Requirement: Vault Key Rotation
The system SHALL support active vault key rotation and background re-encryption of stored vault entries.

#### Scenario: Active key changes persist across restarts
- WHEN an administrator rotates the active vault key
- THEN CardVault persists the active key identifier and restores it on startup

#### Scenario: Background re-encryption progresses in batches
- WHEN stored vault records need to be re-encrypted after a key rotation
- THEN CardVault processes the re-encryption job incrementally without requiring a full-stop migration window

### Requirement: PCI-Safe Audit Events
The system MUST publish PCI-safe audit information for sensitive vault operations.

#### Scenario: Tokenization or detokenization emits an audit event
- WHEN CardVault performs a sensitive vault operation
- THEN it emits an audit record without including raw PAN in the payload
