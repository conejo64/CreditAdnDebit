# Proposal: Robust IsoSwitch Testing (Refactor & Coverage)

## Intent
Unify the testing infrastructure for `IsoSwitch.Tests` and achieve comprehensive coverage for all transaction handlers, specifically focusing on failure paths, timeouts, and state transitions.

## Rationale
Sprint 10 (current phase) aims to close the technical debt of Sprint 8. Current tests are fragmented (using different assertion styles and setup methods) and only cover "happy paths" and basic idempotency. A banking switch needs to be resilient to connector failures, database errors, and invalid ISO responses.

## Scope
- **Project:** `IsoSwitch.Tests`
- **Focus:** `AuthorizeTransaction`, `CaptureTransaction`, `ReversalTransaction`, `ReversalAdvice`, `NetworkManagement` handlers.
- **Cleanup:** Unify all test files to use `FluentAssertions` and `TestDbContextFactory`.

## Approach
1. **Infrastructure Update:** Ensure `TestDbContextFactory` is the single source of truth for DB context creation in tests.
2. **Standardization:** Refactor existing test files to follow the same pattern:
   - Use `Substitute.For` for all dependencies.
   - Use `FluentAssertions` for all checks.
   - Consistent naming convention (`Handle_Condition_ShouldExpectation`).
3. **Failure Path Coverage:**
   - Connector exceptions (timeout, network error) -> Verify `InDoubt` state or transaction rollback.
   - Connector rejections (non-00 RC) -> Verify `Declined` state.
   - Idempotency with existing non-matching state (edge cases).
   - Routing failures.

## Risks
- **In-Memory DB Limitations:** Some EF Core behaviors (like specific SQL constraints) aren't perfectly mirrored in `InMemoryDatabase`.
- **Mock Overkill:** Excessive mocking might hide integration issues between handlers and state machine logic.
