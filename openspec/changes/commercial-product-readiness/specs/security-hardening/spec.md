# Delta for Security Hardening

## ADDED Requirements

### Requirement SEC-13: Commercial API Documentation and Diagnostics Exposure

Commercial-mode services MUST deny anonymous diagnostics and MUST NOT expose Swagger/OpenAPI or an API UI unless explicitly enabled for authenticated, authorized operators. Deployment evidence MUST record enabled API surfaces per environment.

#### Scenario: Commercial default exposure
- GIVEN a service starts in commercial mode without an explicit authorized documentation configuration
- WHEN an anonymous caller requests Swagger/OpenAPI or a diagnostic route
- THEN the caller receives no usable API documentation or diagnostic response

#### Scenario: Authorized non-commercial documentation
- GIVEN a designated non-commercial environment explicitly enables API documentation
- WHEN an authorized operator requests it
- THEN documentation is available without enabling anonymous diagnostics
