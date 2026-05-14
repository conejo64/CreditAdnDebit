# Design: Error Handling & State in IsoSwitch Handlers

## Failure Strategy: "InDoubt" vs "Declined"

In a financial switch, a failure to communicate with the next node (Acquirer/Issuer) doesn't always mean the transaction was "Declined". It might have been processed by the next node but the response was lost (Timeout).

### 1. Connector Exceptions (Timeout/Network)
If `connector.AuthorizeAsync()` throws an exception:
- The handler catches it.
- The transaction state is updated to `InDoubt = true` and `Status = InDoubt`.
- This allows a background worker or a future reversal to know the state is uncertain.
- In the tests, we mock these exceptions using `_connector.AuthorizeAsync(...).Throws<Exception>()`.

### 2. Explicit Rejections
If the connector returns a message with `RC != "00"`:
- The transaction is marked as `Declined`.
- `TransactionStateMachine.EnsureTransition` must be called to validate the move from `Pending` -> `Declined`.

### 3. Database Consistency
To ensure atomicity:
- We save the transaction as `Pending` BEFORE calling the connector.
- We update it AFTER the connector returns.
- If the DB save after the response fails, the system might have a mismatch. However, during unit testing, we verify the `TransactionStatuses` transitions correctly.

## Test Infrastructure

Individual `InMemoryDatabase` instances are created per test class (or per test method if needed) via `TestDbContextFactory`. This avoids contamination between test cases.

## Mocking
- **Connectors:** Mock behavior for different MTIs and RCs.
- **Event Publishers:** Verify that events are published for both success and failure paths.
- **Audit Service:** Verify that both outgoing and incoming messages are logged.
