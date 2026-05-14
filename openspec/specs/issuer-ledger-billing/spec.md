# Issuer Ledger Billing Specification

## Purpose

Describe the issuer-side account lifecycle, ledger posting, and statement behavior owned by CardVault.

## Requirements

### Requirement: Issuer Customer And Card Lifecycle
The system SHALL manage customers, accounts, and card lifecycle operations inside CardVault.

#### Scenario: A new card is issued for an account
- WHEN an authorized operator issues a card for a customer account
- THEN CardVault stores issuer data, associates the card with the account, and returns safe card references

#### Scenario: Card lifecycle actions are recorded
- WHEN an authorized operator activates or blocks a card
- THEN CardVault updates card status and preserves the status history required for auditability

### Requirement: Ledger Posting
The system SHALL maintain signed ledger entries for credit and debit account activity.

#### Scenario: Financial events affect account balance
- WHEN a purchase, payment, fee, or interest posting is recorded
- THEN CardVault writes ledger entries that contribute to the account balance calculation

### Requirement: Statement Generation
The system SHALL generate billing statements using ledger activity and product policy configuration.

#### Scenario: Minimum payment is derived from policy
- WHEN a statement is generated for a billing cycle
- THEN CardVault computes the minimum payment according to the configured policy and caps it to the outstanding balance when required

### Requirement: Billing Policies Remain Configurable
The system MUST allow administrative configuration of product and billing policies without moving ownership outside CardVault.

#### Scenario: Credit policy changes affect future statement generation
- WHEN an authorized administrator updates billing or credit policy settings
- THEN future calculations use the updated policy values
