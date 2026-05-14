## ADDED Requirements

### Requirement: Accounting Journal Generation
The system SHALL generate accounting journal entries for card operations that require financial recognition.

#### Scenario: Ledger activity produces an accounting journal
- WHEN CardVault posts a mapped ledger event such as a purchase, payment, fee, interest, refund, reversal, or chargeback
- THEN CardVault creates a journal entry with a business date, source reference, and balanced journal lines

### Requirement: Configurable Accounting Mappings
The system MUST allow administrative configuration of debit and credit accounts by event type.

#### Scenario: Mapping exists for a ledger event
- WHEN a mapped accounting event is processed
- THEN CardVault resolves the active debit and credit ledger accounts from the configured accounting mapping

### Requirement: Settlement Accounting Entries
The system SHALL support accounting entries for settlement batches.

#### Scenario: Settlement batch creates an accounting journal
- WHEN CardVault runs a settlement batch with an active accounting mapping
- THEN CardVault creates a settlement journal entry associated with the batch reference and business date

### Requirement: Accounting Auditability And Publication
The system MUST retain traceable accounting journal data and publish integration-safe events for posted journals.

#### Scenario: Journal posting is traceable
- WHEN CardVault posts an accounting journal entry
- THEN CardVault stores the source module, source reference, posted timestamp, and journal lines
- AND CardVault publishes an integration event without raw PAN or other PCI-sensitive values
