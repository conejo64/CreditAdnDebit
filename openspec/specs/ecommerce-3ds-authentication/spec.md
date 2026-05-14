# Ecommerce 3DS Authentication Specification

## Purpose

Define how CardVault manages ecommerce 3D Secure challenge initiation, OTP verification, risk evaluation, and operational monitoring.

## Requirements

### Requirement: Ecommerce 3DS Challenge Initiation
The system SHALL allow authorized operators or platform integrations to start a 3D Secure challenge for an ecommerce card transaction.

#### Scenario: Starting a challenge creates a pending authentication record
- WHEN a valid ecommerce 3DS challenge request is submitted for an active card
- THEN CardVault stores a pending challenge with an expiry time, masked customer contact hint, and a risk assessment
- AND the response returns a challenge identifier and the next action required to complete authentication

### Requirement: OTP Verification And Final Decision
The system SHALL validate an OTP for pending ecommerce challenges and produce a final authentication decision.

#### Scenario: Valid OTP authenticates the ecommerce purchase
- WHEN a caller submits the correct OTP before the challenge expires
- THEN CardVault marks the challenge as authenticated
- AND the response includes an approve decision for the ecommerce authentication result

#### Scenario: Invalid OTP attempts are bounded
- WHEN a caller repeatedly submits an invalid OTP for the same challenge
- THEN CardVault increments the attempt counter
- AND CardVault rejects the challenge after the configured maximum attempts

#### Scenario: Expired challenges cannot be completed
- WHEN a caller submits an OTP after the challenge expiry time
- THEN CardVault rejects the verification request and marks the challenge as expired

### Requirement: Ecommerce Risk Assessment And Monitoring
The system MUST retain PCI-safe monitoring data for ecommerce authentication flows.

#### Scenario: Challenge monitoring exposes operational state without raw PAN
- WHEN an audit or operations user queries ecommerce 3DS challenge history
- THEN CardVault returns challenge status, risk score, risk reasons, merchant context, and timestamps
- AND the monitoring response excludes raw PAN and OTP values

#### Scenario: 3DS lifecycle actions emit PCI-safe audit records
- WHEN CardVault starts, authenticates, rejects, or expires a challenge
- THEN CardVault emits an audit record for the lifecycle transition
- AND the audit payload excludes raw PAN and OTP values
