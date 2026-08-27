</Agent System Instructions>
<Delta for vault-and-pci>
## ADDED Requirements

### Requirement: HSM-Backed MAC Generation

The system SHALL utilize hardware security module (HSM) integration for MAC (Message Authentication Code) generation within `MacService`, ensuring that key material used for MAC generation is never exposed in memory.

#### Scenario: Generating a MAC for a message

- GIVEN a payload requiring a MAC
- WHEN `MacService` is invoked to generate the MAC
- THEN the service delegates the cryptographic operation to the integrated HSM
- AND returns the resulting MAC without exposing the underlying key material in memory

#### Scenario: HSM integration failure during MAC generation

- GIVEN the integrated HSM is unreachable or returns an error
- WHEN `MacService` is invoked to generate a MAC
- THEN the operation fails securely
- AND the system does not fallback to insecure or simulated MAC generation
</Delta for vault-and-pci>
