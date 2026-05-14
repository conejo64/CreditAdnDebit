# Tasks: Robust IsoSwitch Testing

- [ ] **Infrastructure & Standardization**
  - [ ] Migrate `AuthorizeTransactionCommandHandlerTests.cs` to use `FluentAssertions` and `TestDbContextFactory`.
  - [ ] Standardize dependencies in all handlers (using `NSubstitute`).

- [ ] **AuthorizeTransaction Handler Tests**
  - [ ] Add test for connector failure (exception) -> Verify `InDoubt` status.
  - [ ] Add test for connector decline (RC != 00) -> Verify `Declined` status.
  - [ ] Add test for routing resolution failure.

- [ ] **CaptureTransaction Handler Tests**
  - [ ] Migrate to `FluentAssertions` / `TestDbContextFactory`.
  - [ ] Add test for connector exception.
  - [ ] Add test for decline response.

- [ ] **ReversalTransaction Handler Tests**
  - [ ] Verify original transaction lookup fails (Not Found).
  - [ ] Verify already reversed idempotency.
  - [ ] Add test for connector error.

- [ ] **ReversalAdvice Handler Tests**
  - [ ] Verify original transaction update to `REVERSAL_CONFIRMED`.
  - [ ] Add test for decline response (RC != 00).
  - [ ] Add test for connector exception.

- [ ] **NetworkManagement Handler Tests**
  - [ ] Verify Ping, SignOn, SignOff messages are correctly formatted.
  - [ ] Verify connector failure handling.

- [ ] **Verification**
  - [ ] Run all tests and ensure 100% pass rate.
  - [ ] Verify in-memory DB isolation (no cross-test pollution).
