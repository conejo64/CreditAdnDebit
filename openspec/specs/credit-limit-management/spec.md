## ADDED Requirements

### Requirement: Controlled Overlimit Authorization
The system SHALL allow overlimit authorizations only within a configured and traceable tolerance.

#### Scenario: Authorization exceeds available credit within allowed buffer
- WHEN a credit account authorization exceeds current available credit
- AND the product policy allows controlled overlimit within the configured buffer
- THEN CardVault approves the authorization, records the approved excess amount, and preserves traceability for fee assessment and audit

#### Scenario: Authorization exceeds allowed overlimit buffer
- WHEN a credit account authorization exceeds available credit beyond the configured overlimit buffer
- THEN CardVault declines the authorization

### Requirement: Credit Limit Increase Evaluation
The system SHALL evaluate recent payment behavior and credit utilization to generate credit limit proposals.

#### Scenario: Account qualifies for increase proposal
- WHEN an authorized internal user evaluates a credit account with sufficient payment history
- AND the account meets configured payment and utilization thresholds
- THEN CardVault creates or returns a proposal with the current limit, suggested increase, and supporting metrics

### Requirement: Credit Limit Proposal Application
The system SHALL allow authorized internal users to apply an approved credit limit proposal.

#### Scenario: Proposal is applied
- WHEN an authorized internal user applies a pending credit limit proposal
- THEN CardVault updates the account credit limit and available limit
- AND CardVault records the proposal status and applied timestamp
