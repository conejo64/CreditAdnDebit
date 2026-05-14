# Proposal: Frontend Mock Cleanup and Robust Error Handling

## Root Cause
Several Angular components (notably `CustomerListComponent` and `SimulatorComponent`) are using `catchError(() => of(...))` with hardcoded mock data. This creates a "silent failure" UX where the user sees fake data if the backend is down, leading to confusion and potential data integrity issues in manual testing.

## Proposed Changes
1. **Remove Mock Fallbacks**: Eliminate all `catchError` blocks that return hardcoded arrays of data for core business entities (cards, customers, catalogs).
2. **Notification System**: Implement a lightweight `NotificationService` in the `core` folder.
3. **Visual Feedback**:
    - Show an error "toast" or notice when an API request fails.
    - Keep fields empty or in an error state instead of populated with mocks.
4. **Standardize Error Handling**: Use a consistent pattern for handling API errors across the application.

## High-Level Tasks
- [ ] Create `NotificationService` for global toast notifications.
- [ ] Implement a `NotificationComponent` (or add logic to `AppComponent`) to render toasts.
- [ ] Refactor `CustomerListComponent` to remove mock catalogs and customers fallbacks.
- [ ] Refactor `SimulatorComponent` to handle data loading failures.
- [ ] Audit other components (`CardListComponent`, `AuditListComponent`, etc.) for similar patterns.

## Risks
- If the backend is genuinely down during a demo, the UI will look empty. This is the desired behavior but must be communicated clearly via UI.
