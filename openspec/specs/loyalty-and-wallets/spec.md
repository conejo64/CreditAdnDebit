## ADDED Requirements

### Requirement: Loyalty Accumulation
The system SHALL calculate and register loyalty rewards for eligible card purchases.

#### Scenario: Purchase accrues cashback and points
- WHEN CardVault processes an eligible card purchase for an account with an active rewards program
- THEN CardVault calculates cashback and points using the configured product program
- AND CardVault records the accumulation in the loyalty balance and transaction history

### Requirement: Loyalty Redemption And Catalog
The system SHALL allow consultation and redemption of configured rewards.

#### Scenario: Customer balance is queried
- WHEN an authorized internal user requests the loyalty balance for an account
- THEN CardVault returns the current cashback balance, points balance, and recent reward entries

#### Scenario: Reward is redeemed
- WHEN an authorized internal user redeems a reward catalog item for an eligible account
- THEN CardVault validates the available balance, deducts the configured value, and records the redemption

### Requirement: Wallet Enrollment
The system SHALL support digital wallet card enrollment with activation control.

#### Scenario: Card is enrolled into a wallet
- WHEN an authorized internal user registers an eligible card for a supported wallet provider
- THEN CardVault creates a wallet token enrollment with an activation challenge and pending status

#### Scenario: Wallet token is activated
- WHEN the correct activation challenge is provided before expiry
- THEN CardVault activates the wallet token and marks it available for wallet payments

### Requirement: Wallet Payment Authorization
The system SHALL authorize wallet payments using active wallet tokens and existing risk controls.

#### Scenario: Wallet payment is requested
- WHEN a wallet authorization request references an active wallet token with successful device authentication
- THEN CardVault validates the wallet token, applies existing authorization controls, and returns an auditable approval or decline result

### Requirement: PCI-Safe Loyalty And Wallet Events
The system MUST preserve PCI boundaries when publishing or exposing loyalty and wallet data.

#### Scenario: Integration events are published
- WHEN CardVault records loyalty or wallet activity
- THEN the published event contains safe identifiers and status metadata only
- AND CardVault does not expose raw PAN or sensitive authentication payloads
