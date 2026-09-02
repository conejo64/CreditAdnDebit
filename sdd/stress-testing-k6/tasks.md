<Tasks: stress-testing-k6>
## Review Workload Forecast
Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: auto-chain
400-line budget risk: Low

## Phase 1: Foundation
- [x] 1.1 Create K6 directory structure `backend/services/IsoSwitch/tests/k6/`
- [x] 1.2 Write README instructions in `backend/services/IsoSwitch/tests/k6/README.md` for running tests

## Phase 2: Core Implementation
- [x] 2.1 Write payload generator module for `AuthorizeTransaction` mapping JSON structure to ISO8583 fields
- [x] 2.2 Create load test script `isoswitch-load.js` with dynamic targeting via base URL environment variable
- [x] 2.3 Implement load profiles (smoke, soak, stress) in `isoswitch-load.js`
- [x] 2.4 Add metrics tracking (95th/99th percentile response times, failure rate)
</Tasks: stress-testing-k6>
