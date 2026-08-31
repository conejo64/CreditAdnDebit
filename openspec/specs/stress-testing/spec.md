<stress-testing Specification>
## Purpose

Defines the requirements for K6-based stress testing, including load profiles, metrics tracking, and target configurations.

## Requirements

### Requirement: Load Profiles

The system MUST support predefined load profiles: smoke, soak, and stress.

#### Scenario: Smoke Test

- GIVEN the test is configured for a smoke profile
- WHEN the test is executed
- THEN a minimal number of virtual users SHOULD be applied for a short duration
- AND the test MUST verify basic endpoint functionality

#### Scenario: Soak Test

- GIVEN the test is configured for a soak profile
- WHEN the test is executed
- THEN a moderate, constant load MUST be maintained over an extended period
- AND the system SHOULD NOT exhibit memory leaks or performance degradation

#### Scenario: Stress Test

- GIVEN the test is configured for a stress profile
- WHEN the test is executed
- THEN the system MUST be subjected to a gradually increasing load until saturation
- AND the test MUST record the breaking point metrics

### Requirement: Metrics Tracking

The system MUST track and report specific performance metrics during test execution.

#### Scenario: Response Time Tracking

- GIVEN a test is running
- WHEN HTTP requests are made
- THEN the test MUST record the 95th and 99th percentile response times
- AND fail the test if the 95th percentile exceeds the defined threshold

#### Scenario: Failure Rate Tracking

- GIVEN a test is running
- WHEN requests receive non-200 responses or timeout
- THEN the failure rate MUST be calculated
- AND fail the test if the failure rate exceeds the defined threshold (e.g., 1%)

### Requirement: HTTP Endpoint Targeting

The test scripts MUST be configurable to target specific HTTP endpoints dynamically.

#### Scenario: Dynamic Targeting

- GIVEN a test script
- WHEN an environment variable specifies the target base URL
- THEN all requests MUST be routed to the specified URL
</stress-testing Specification>
