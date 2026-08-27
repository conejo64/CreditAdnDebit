## Purpose

Defines the requirements for PIN block parsing and MAC generation using hardware security modules (HSMs).

## Requirements

### Requirement: PIN Block Parsing

The system MUST parse incoming encrypted PIN blocks using the configured HSM without exposing the clear PIN.

#### Scenario: Valid PIN block parsing

- GIVEN a valid encrypted PIN block and corresponding account details
- WHEN the system parses the PIN block via the HSM
- THEN the HSM processes the request
- AND the system receives a secure confirmation without the clear PIN in memory

#### Scenario: Invalid PIN block parsing fails

- GIVEN an invalid or corrupted encrypted PIN block
- WHEN the system attempts to parse the PIN block via the HSM
- THEN the operation fails securely
- AND an error is returned without exposing any PIN data

### Requirement: MAC Generation

The system MUST generate MACs (Message Authentication Codes) for required payloads using the configured HSM.

#### Scenario: Successful MAC generation

- GIVEN a payload requiring a MAC
- WHEN the system requests MAC generation
- THEN the HSM generates the MAC using secure key material
- AND the resulting MAC is returned without exposing the key in memory

#### Scenario: HSM failure during MAC generation

- GIVEN the HSM is unavailable or returns an error
- WHEN the system requests MAC generation
- THEN the operation fails securely
- AND no insecure fallback MAC is generated
