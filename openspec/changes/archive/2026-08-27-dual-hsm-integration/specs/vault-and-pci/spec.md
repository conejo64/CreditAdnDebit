## MODIFIED Requirements

### Requirement: HSM-Backed MAC Generation

The system SHALL utilize hardware security module (HSM) integration for MAC (Message Authentication Code) generation by consuming `IHsmService`, ensuring that key material used for MAC generation is never exposed in memory.
(Previously: MAC generation was handled directly within MacService instead of abstracting through IHsmService)

#### Scenario: Generating a MAC for a message

- GIVEN a payload requiring a MAC
- WHEN `MacService` is invoked to generate the MAC
- THEN the service delegates the cryptographic operation to the integrated HSM via `IHsmService`
- AND returns the resulting MAC without exposing the underlying key material in memory

#### Scenario: HSM integration failure during MAC generation

- GIVEN the integrated HSM is unreachable or returns an error
- WHEN `MacService` is invoked to generate a MAC via `IHsmService`
- THEN the operation fails securely
- AND the system does not fallback to insecure or simulated MAC generation
